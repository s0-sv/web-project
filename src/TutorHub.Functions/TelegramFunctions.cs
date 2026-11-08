using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using TutorHub.Integrations;

namespace TutorHub.Functions;

public class TelegramWebhookResult
{
    [QueueOutput(QueueNames.TelegramUpdates)]
    public byte[]? Update { get; set; }

    [HttpResult]
    public IActionResult Response { get; set; } = new OkResult();
}

public record RegisterWebhookRequest(string? Url);

public class TelegramFunctions(TelegramBotClient telegram, TelegramSettings settings, ILogger<TelegramFunctions> logger)
{
    public const string SecretHeader = "X-Telegram-Bot-Api-Secret-Token";
    public const string WebhookRoute = "telegram/webhook";
    public const int MaxUpdateBytes = 48 * 1024;

    [Function("TelegramWebhook")]
    public async Task<TelegramWebhookResult> Webhook(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = WebhookRoute)] HttpRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(settings.WebhookSecret))
        {
            logger.LogError("Telegram webhook called but Telegram:WebhookSecret is not configured");
            return new TelegramWebhookResult { Response = new StatusCodeResult(StatusCodes.Status503ServiceUnavailable) };
        }

        if (!SecretMatches(request.Headers[SecretHeader].ToString(), settings.WebhookSecret))
        {
            logger.LogWarning("Telegram webhook rejected: secret token mismatch");
            return new TelegramWebhookResult { Response = new UnauthorizedResult() };
        }

        var body = await ReadLimitedAsync(request.Body, MaxUpdateBytes, ct);
        if (body is null)
        {
            logger.LogWarning("Telegram update dropped: larger than {Limit} bytes", MaxUpdateBytes);
            return new TelegramWebhookResult();
        }

        if (!IsUpdate(body))
        {
            return new TelegramWebhookResult { Response = new BadRequestResult() };
        }

        return new TelegramWebhookResult { Update = Encoding.UTF8.GetBytes(body) };
    }

    [Function("RegisterTelegramWebhook")]
    public async Task<IActionResult> Register(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "telegram/register")] HttpRequest request,
        CancellationToken ct)
    {
        if (!settings.HasBotToken || string.IsNullOrEmpty(settings.WebhookSecret))
        {
            return Problem(StatusCodes.Status503ServiceUnavailable, "Telegram:BotToken and Telegram:WebhookSecret must be configured.");
        }

        RegisterWebhookRequest? input = null;
        if (request.ContentLength is > 0)
        {
            try
            {
                input = await JsonSerializer.DeserializeAsync<RegisterWebhookRequest>(request.Body, QueueJson.Options, ct);
            }
            catch (JsonException)
            {
                return Problem(StatusCodes.Status400BadRequest, "Body must be JSON: {\"url\": \"https://...\"}.");
            }
        }

        var url = string.IsNullOrWhiteSpace(input?.Url)
            ? $"{request.Scheme}://{request.Host}/api/{WebhookRoute}"
            : input.Url.Trim();

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            return Problem(StatusCodes.Status400BadRequest, $"Telegram requires an HTTPS webhook URL, got '{url}'.");
        }

        try
        {
            await telegram.SetWebhookAsync(uri.ToString(), settings.WebhookSecret, ct);
        }
        catch (TelegramApiException e)
        {
            logger.LogError("setWebhook failed: {Code} {Description}", e.ErrorCode, e.Description);
            return Problem(StatusCodes.Status502BadGateway, $"Telegram rejected the webhook: {e.Description}");
        }

        logger.LogInformation("Telegram webhook registered at {Url}", uri);
        return new OkObjectResult(new { ok = true, url = uri.ToString(), allowedUpdates = TelegramBotClient.AllowedUpdates });
    }

    [Function("SendTelegramMessage")]
    public async Task Send(
        [QueueTrigger(QueueNames.TelegramOutbox)] string payload,
        CancellationToken ct)
    {
        var message = QueueJson.Deserialize<TelegramOutgoingMessage>(payload);
        if (message.ChatId == 0 || string.IsNullOrWhiteSpace(message.Text))
        {
            throw new InvalidOperationException("Telegram message must have a chat id and text.");
        }

        try
        {
            await telegram.SendMessageAsync(message.ChatId, message.Text, ct);
        }
        catch (TelegramApiException e) when (e.IsPermanent)
        {
            logger.LogWarning("Telegram message to chat {ChatId} dropped: {Code} {Description}",
                message.ChatId, e.ErrorCode, e.Description);
        }
    }

    [Function("Health")]
    public IActionResult Health(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "health")] HttpRequest request) =>
        new OkObjectResult(new
        {
            status = "ok",
            telegramConfigured = settings.HasBotToken,
            webhookSecretConfigured = !string.IsNullOrEmpty(settings.WebhookSecret)
        });

    public static bool SecretMatches(string provided, string expected) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(provided), Encoding.UTF8.GetBytes(expected));

    private static bool IsUpdate(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("update_id", out var id)
                && id.ValueKind == JsonValueKind.Number
                && id.TryGetInt64(out _);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static async Task<string?> ReadLimitedAsync(Stream stream, int limit, CancellationToken ct)
    {
        var buffer = new byte[limit + 1];
        var total = 0;
        int read;
        while (total <= limit && (read = await stream.ReadAsync(buffer.AsMemory(total, buffer.Length - total), ct)) > 0)
        {
            total += read;
        }

        return total > limit ? null : Encoding.UTF8.GetString(buffer, 0, total);
    }

    private static ObjectResult Problem(int status, string detail) =>
        new(new ProblemDetails { Status = status, Detail = detail }) { StatusCode = status };
}
