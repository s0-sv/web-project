using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TutorHub.Integrations;

public record TelegramOutgoingMessage(long ChatId, string Text);

public class TelegramSettings
{
    public const string Section = "Telegram";

    public string? BotToken { get; set; }

    public string? WebhookSecret { get; set; }

    public Uri? ApiBaseUrl { get; set; }

    public bool HasBotToken => !string.IsNullOrWhiteSpace(BotToken);
}

public class TelegramApiException(int errorCode, string description, TimeSpan? retryAfter)
    : Exception($"Telegram API error {errorCode}: {description}")
{
    public int ErrorCode { get; } = errorCode;

    public string Description { get; } = description;

    public TimeSpan? RetryAfter { get; } = retryAfter;

    public bool IsPermanent => ErrorCode is 400 or 403;
}

public record TelegramBotInfo(long Id, string Username);

public class TelegramBotClient(HttpClient http, TelegramSettings settings)
{
    public const string HttpClientName = "telegram";
    public const int MaxTextLength = 4096;

    public static readonly Uri BaseAddress = new("https://api.telegram.org/");

    public static IReadOnlyList<string> AllowedUpdates { get; } = ["message", "my_chat_member"];

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string Truncate(string text)
    {
        if (text.Length <= MaxTextLength)
        {
            return text;
        }

        var length = MaxTextLength - 1;
        if (char.IsHighSurrogate(text[length - 1]))
        {
            length--;
        }

        return string.Concat(text.AsSpan(0, length), "…");
    }

    public Task SendMessageAsync(long chatId, string text, CancellationToken ct = default) =>
        CallAsync("sendMessage", new
        {
            ChatId = chatId,
            Text = Truncate(text),
            LinkPreviewOptions = new { IsDisabled = true }
        }, ct);

    public Task SetWebhookAsync(string url, string secret, CancellationToken ct = default) =>
        CallAsync("setWebhook", new
        {
            Url = url,
            SecretToken = secret,
            AllowedUpdates
        }, ct);

    public Task DeleteWebhookAsync(CancellationToken ct = default) =>
        CallAsync("deleteWebhook", new { DropPendingUpdates = false }, ct);

    public async Task<TelegramBotInfo> GetMeAsync(CancellationToken ct = default)
    {
        var result = await CallAsync("getMe", new { }, ct);
        return new TelegramBotInfo(result.GetProperty("id").GetInt64(), result.GetProperty("username").GetString() ?? "");
    }

    public async Task<IReadOnlyList<JsonElement>> GetUpdatesAsync(long offset, int timeoutSeconds, CancellationToken ct = default)
    {
        var result = await CallAsync("getUpdates", new
        {
            Offset = offset,
            Timeout = timeoutSeconds,
            AllowedUpdates
        }, ct);

        return result.EnumerateArray().Select(u => u.Clone()).ToList();
    }

    private async Task<JsonElement> CallAsync(string method, object body, CancellationToken ct)
    {
        if (!settings.HasBotToken)
        {
            throw new InvalidOperationException("Telegram:BotToken is not configured.");
        }

        using var response = await http.PostAsJsonAsync($"./bot{settings.BotToken}/{method}", body, Json, ct);
        JsonElement payload;
        try
        {
            payload = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        }
        catch (JsonException)
        {
            throw new TelegramApiException((int)response.StatusCode, "Response is not JSON", null);
        }

        if (payload.ValueKind == JsonValueKind.Object
            && payload.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True
            && payload.TryGetProperty("result", out var result))
        {
            return result.Clone();
        }

        var code = payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("error_code", out var c) && c.TryGetInt32(out var parsed)
            ? parsed
            : (int)response.StatusCode;
        var description = payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("description", out var d)
            ? d.GetString() ?? ""
            : "";
        TimeSpan? retryAfter = payload.ValueKind == JsonValueKind.Object
            && payload.TryGetProperty("parameters", out var p)
            && p.TryGetProperty("retry_after", out var r)
            && r.TryGetInt32(out var seconds)
                ? TimeSpan.FromSeconds(seconds)
                : null;

        throw new TelegramApiException(code, description, retryAfter);
    }
}
