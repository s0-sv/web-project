using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using TutorHub.Functions;
using TutorHub.Integrations;
using Xunit;

namespace TutorHub.FunctionsTests;

public class FakeTelegramHandler : HttpMessageHandler
{
    public List<(string Path, JsonElement Body)> Requests { get; } = [];

    public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

    public string Response { get; set; } = """{"ok":true,"result":true}""";

    public Exception? Throw { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (Throw is not null)
        {
            throw Throw;
        }

        var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)).RootElement.Clone();
        Requests.Add((request.RequestUri!.AbsolutePath, body));
        return new HttpResponseMessage(Status) { Content = new StringContent(Response, Encoding.UTF8, "application/json") };
    }
}

public class TelegramFunctionsTests
{
    private const string Token = "123:test-token";
    private const string Secret = "webhook-secret_1";

    private readonly FakeTelegramHandler _handler = new();

    private TelegramFunctions Functions(string? token = Token, string? secret = Secret)
    {
        var settings = new TelegramSettings { BotToken = token, WebhookSecret = secret };
        var client = new TelegramBotClient(new HttpClient(_handler) { BaseAddress = TelegramBotClient.BaseAddress }, settings);
        return new TelegramFunctions(client, settings, NullLogger<TelegramFunctions>.Instance);
    }

    private static HttpRequest Request(string body = "", string? secret = Secret, string host = "tutorhub-fn.azurewebsites.net", string scheme = "https")
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Scheme = scheme;
        context.Request.Host = new HostString(host);
        var bytes = Encoding.UTF8.GetBytes(body);
        context.Request.Body = new MemoryStream(bytes);
        context.Request.ContentLength = bytes.Length;
        if (secret is not null)
        {
            context.Request.Headers[TelegramFunctions.SecretHeader] = secret;
        }

        return context.Request;
    }

    private const string Update = """{"update_id":42,"message":{"message_id":1,"chat":{"id":777,"type":"private"},"text":"/start abc"}}""";

    [Fact]
    public async Task Webhook_with_valid_secret_enqueues_the_raw_update()
    {
        var result = await Functions().Webhook(Request(Update), CancellationToken.None);

        Assert.IsType<OkResult>(result.Response);
        Assert.Equal(Update, Encoding.UTF8.GetString(result.Update!));
        Assert.Empty(_handler.Requests);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("webhook-secret_2")]
    [InlineData("webhook-secret_1 ")]
    [InlineData("WEBHOOK-SECRET_1")]
    public async Task Webhook_rejects_missing_or_wrong_secret(string? secret)
    {
        var result = await Functions().Webhook(Request(Update, secret), CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(result.Response);
        Assert.Null(result.Update);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task Webhook_refuses_everything_when_secret_is_not_configured(string? configured)
    {
        var result = await Functions(secret: configured).Webhook(Request(Update, secret: ""), CancellationToken.None);

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, Assert.IsType<StatusCodeResult>(result.Response).StatusCode);
        Assert.Null(result.Update);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{"message":{}}""")]
    [InlineData("""{"update_id":"42"}""")]
    [InlineData("")]
    public async Task Webhook_rejects_bodies_that_are_not_telegram_updates(string body)
    {
        var result = await Functions().Webhook(Request(body), CancellationToken.None);

        Assert.IsType<BadRequestResult>(result.Response);
        Assert.Null(result.Update);
    }

    [Fact]
    public async Task Webhook_drops_oversized_update_but_acknowledges_it()
    {
        var huge = """{"update_id":1,"message":{"text":" """.TrimEnd() + new string('x', TelegramFunctions.MaxUpdateBytes) + "\"}}";

        var result = await Functions().Webhook(Request(huge), CancellationToken.None);

        Assert.IsType<OkResult>(result.Response);
        Assert.Null(result.Update);
    }

    [Fact]
    public async Task Webhook_accepts_update_exactly_at_the_size_limit()
    {
        var prefix = """{"update_id":1,"message":{"text":" """.TrimEnd();
        var suffix = "\"}}";
        var body = prefix + new string('x', TelegramFunctions.MaxUpdateBytes - prefix.Length - suffix.Length) + suffix;
        Assert.Equal(TelegramFunctions.MaxUpdateBytes, Encoding.UTF8.GetByteCount(body));

        var result = await Functions().Webhook(Request(body), CancellationToken.None);

        Assert.Equal(body, Encoding.UTF8.GetString(result.Update!));
    }

    [Fact]
    public void Webhook_result_writes_to_the_updates_queue()
    {
        var attribute = typeof(TelegramWebhookResult).GetProperty(nameof(TelegramWebhookResult.Update))!
            .GetCustomAttributes(typeof(Microsoft.Azure.Functions.Worker.QueueOutputAttribute), false)
            .Cast<Microsoft.Azure.Functions.Worker.QueueOutputAttribute>()
            .Single();

        Assert.Equal(QueueNames.TelegramUpdates, attribute.QueueName);
    }

    [Fact]
    public async Task Register_derives_https_url_and_sends_secret_and_allowed_updates()
    {
        var result = await Functions().Register(Request(), CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        var (path, body) = Assert.Single(_handler.Requests);
        Assert.Equal($"/bot{Token}/setWebhook", path);
        Assert.Equal("https://tutorhub-fn.azurewebsites.net/api/telegram/webhook", body.GetProperty("url").GetString());
        Assert.Equal(Secret, body.GetProperty("secret_token").GetString());
        Assert.Equal(["message", "my_chat_member"], body.GetProperty("allowed_updates").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public async Task Register_uses_url_from_body()
    {
        await Functions().Register(Request("""{"url":"https://example.ngrok.app/api/telegram/webhook"}"""), CancellationToken.None);

        Assert.Equal("https://example.ngrok.app/api/telegram/webhook",
            Assert.Single(_handler.Requests).Body.GetProperty("url").GetString());
    }

    [Theory]
    [InlineData("""{"url":"http://example.com/hook"}""")]
    [InlineData("""{"url":"not a url"}""")]
    [InlineData("{oops")]
    public async Task Register_rejects_non_https_or_invalid_input(string body)
    {
        var result = await Functions().Register(Request(body), CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsType<ObjectResult>(result).StatusCode);
        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task Register_rejects_derived_http_url()
    {
        var result = await Functions().Register(Request(scheme: "http", host: "localhost:7071"), CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsType<ObjectResult>(result).StatusCode);
    }

    [Theory]
    [InlineData(null, Secret)]
    [InlineData(Token, null)]
    public async Task Register_requires_token_and_secret(string? token, string? secret)
    {
        var result = await Functions(token, secret).Register(Request(), CancellationToken.None);

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, Assert.IsType<ObjectResult>(result).StatusCode);
    }

    [Fact]
    public async Task Register_reports_telegram_rejection_as_bad_gateway()
    {
        _handler.Status = HttpStatusCode.BadRequest;
        _handler.Response = """{"ok":false,"error_code":400,"description":"Bad Request: bad webhook"}""";

        var result = await Functions().Register(Request(), CancellationToken.None);

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status502BadGateway, problem.StatusCode);
        Assert.Contains("bad webhook", Assert.IsType<ProblemDetails>(problem.Value).Detail);
    }

    [Fact]
    public async Task Send_posts_message_without_link_preview()
    {
        await Functions().Send(QueueJson.Serialize(new TelegramOutgoingMessage(777, "Заняття о 16:00")), CancellationToken.None);

        var (path, body) = Assert.Single(_handler.Requests);
        Assert.Equal($"/bot{Token}/sendMessage", path);
        Assert.Equal(777, body.GetProperty("chat_id").GetInt64());
        Assert.Equal("Заняття о 16:00", body.GetProperty("text").GetString());
        Assert.True(body.GetProperty("link_preview_options").GetProperty("is_disabled").GetBoolean());
        Assert.False(body.TryGetProperty("parse_mode", out _));
    }

    [Theory]
    [InlineData(403, "Forbidden: bot was blocked by the user")]
    [InlineData(400, "Bad Request: chat not found")]
    public async Task Send_completes_on_permanent_errors_so_message_is_not_retried(int code, string description)
    {
        _handler.Status = (HttpStatusCode)code;
        _handler.Response = $$"""{"ok":false,"error_code":{{code}},"description":"{{description}}"}""";

        await Functions().Send(QueueJson.Serialize(new TelegramOutgoingMessage(777, "hi")), CancellationToken.None);

        Assert.Single(_handler.Requests);
    }

    [Fact]
    public async Task Send_throws_on_rate_limit_so_queue_retries()
    {
        _handler.Status = HttpStatusCode.TooManyRequests;
        _handler.Response = """{"ok":false,"error_code":429,"description":"Too Many Requests: retry after 7","parameters":{"retry_after":7}}""";

        var error = await Assert.ThrowsAsync<TelegramApiException>(() =>
            Functions().Send(QueueJson.Serialize(new TelegramOutgoingMessage(777, "hi")), CancellationToken.None));

        Assert.Equal(429, error.ErrorCode);
        Assert.Equal(TimeSpan.FromSeconds(7), error.RetryAfter);
        Assert.False(error.IsPermanent);
    }

    [Fact]
    public async Task Send_throws_on_server_error_and_non_json_response()
    {
        _handler.Status = HttpStatusCode.BadGateway;
        _handler.Response = "<html>bad gateway</html>";

        var error = await Assert.ThrowsAsync<TelegramApiException>(() =>
            Functions().Send(QueueJson.Serialize(new TelegramOutgoingMessage(777, "hi")), CancellationToken.None));

        Assert.Equal(502, error.ErrorCode);
    }

    [Fact]
    public async Task Send_throws_on_network_failure()
    {
        _handler.Throw = new HttpRequestException("connection refused");

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            Functions().Send(QueueJson.Serialize(new TelegramOutgoingMessage(777, "hi")), CancellationToken.None));
    }

    [Theory]
    [InlineData("""{"chatId":0,"text":"hi"}""")]
    [InlineData("""{"chatId":5,"text":"  "}""")]
    [InlineData("null")]
    public async Task Send_throws_on_invalid_message_so_it_ends_in_poison_queue(string payload)
    {
        await Assert.ThrowsAnyAsync<Exception>(() => Functions().Send(payload, CancellationToken.None));
        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task Send_without_token_fails_and_never_calls_telegram()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Functions(token: null).Send(QueueJson.Serialize(new TelegramOutgoingMessage(777, "hi")), CancellationToken.None));
        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task Exception_message_never_contains_the_bot_token()
    {
        _handler.Status = HttpStatusCode.Unauthorized;
        _handler.Response = """{"ok":false,"error_code":401,"description":"Unauthorized"}""";

        var error = await Assert.ThrowsAsync<TelegramApiException>(() =>
            Functions().Send(QueueJson.Serialize(new TelegramOutgoingMessage(777, "hi")), CancellationToken.None));

        Assert.DoesNotContain(Token, error.ToString());
    }

    [Fact]
    public void Health_reports_configuration_without_values()
    {
        var result = Assert.IsType<OkObjectResult>(Functions(secret: null).Health(Request()));
        var json = JsonSerializer.Serialize(result.Value);

        Assert.Contains("\"telegramConfigured\":true", json);
        Assert.Contains("\"webhookSecretConfigured\":false", json);
        Assert.DoesNotContain(Token, json);
    }

    [Theory]
    [InlineData(4096, 4096)]
    [InlineData(5000, 4096)]
    public void Long_text_is_truncated_to_the_telegram_limit(int length, int expected)
    {
        var text = TelegramBotClient.Truncate(new string('a', length));

        Assert.Equal(expected, text.Length);
        Assert.Equal(length > 4096, text.EndsWith('…'));
    }

    [Fact]
    public void Truncation_never_splits_a_surrogate_pair()
    {
        var text = TelegramBotClient.Truncate(new string('a', 4094) + "😀😀😀");

        Assert.True(text.Length <= TelegramBotClient.MaxTextLength);
        Assert.False(char.IsHighSurrogate(text[^2]));
        Assert.EndsWith("…", text);
    }

    [Fact]
    public async Task Get_updates_returns_each_update_and_get_me_reads_username()
    {
        _handler.Response = """{"ok":true,"result":[{"update_id":1},{"update_id":2}]}""";
        var settings = new TelegramSettings { BotToken = Token };
        var client = new TelegramBotClient(new HttpClient(_handler) { BaseAddress = TelegramBotClient.BaseAddress }, settings);

        var updates = await client.GetUpdatesAsync(5, 25);

        Assert.Equal([1L, 2L], updates.Select(u => u.GetProperty("update_id").GetInt64()));
        var body = Assert.Single(_handler.Requests).Body;
        Assert.Equal(5, body.GetProperty("offset").GetInt64());
        Assert.Equal(25, body.GetProperty("timeout").GetInt32());

        _handler.Response = """{"ok":true,"result":{"id":99,"is_bot":true,"username":"tutorhub_bot"}}""";
        Assert.Equal(new TelegramBotInfo(99, "tutorhub_bot"), await client.GetMeAsync());
    }
}
