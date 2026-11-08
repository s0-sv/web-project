using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using TutorHub.Integrations;
using TutorHub.Services;
using Xunit;

namespace TutorHub.AuthTests;

public class TelegramPollingTests
{
    private sealed class ScriptedTelegram : HttpMessageHandler
    {
        public ConcurrentQueue<(string Method, JsonElement Body)> Calls { get; } = new();

        private int _updatesServed;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var method = request.RequestUri!.AbsolutePath.Split('/')[^1];
            var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)).RootElement.Clone();
            Calls.Enqueue((method, body));

            var result = method switch
            {
                "getUpdates" when Interlocked.Increment(ref _updatesServed) == 1 =>
                    """[{"update_id":100,"message":{"text":"a"}},{"update_id":101,"message":{"text":"b"}}]""",
                "getUpdates" => await EmptyAfterDelayAsync(ct),
                _ => "true"
            };

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($$"""{"ok":true,"result":{{result}}}""", Encoding.UTF8, "application/json")
            };
        }

        private static async Task<string> EmptyAfterDelayAsync(CancellationToken ct)
        {
            await Task.Delay(50, ct);
            return "[]";
        }
    }

    private sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(handler, disposeHandler: false) { BaseAddress = TelegramBotClient.BaseAddress };
    }

    [Fact]
    public async Task Polling_removes_webhook_forwards_updates_and_advances_offset()
    {
        var telegram = new ScriptedTelegram();
        var queue = new FakeMessageQueue();
        var service = new TelegramPollingService(
            new TelegramClients(new SingleClientFactory(telegram), new TelegramSettings { BotToken = "1:polling" }),
            queue,
            NullLogger<TelegramPollingService>.Instance);

        await service.StartAsync(CancellationToken.None);
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (telegram.Calls.Count(c => c.Method == "getUpdates") < 3 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        await service.StopAsync(CancellationToken.None);

        var calls = telegram.Calls.ToList();
        Assert.Equal("deleteWebhook", calls[0].Method);
        Assert.Single(calls, c => c.Method == "deleteWebhook");
        var polls = calls.Where(c => c.Method == "getUpdates").Select(c => c.Body).ToList();
        Assert.Equal(0, polls[0].GetProperty("offset").GetInt64());
        Assert.Equal(102, polls[1].GetProperty("offset").GetInt64());
        Assert.Equal(TelegramPollingService.LongPollSeconds, polls[0].GetProperty("timeout").GetInt32());

        var sent = queue.Sent.Where(s => s.Queue == QueueNames.TelegramUpdates).Select(s => s.Payload).ToList();
        Assert.Equal(2, sent.Count);
        Assert.Contains(sent, p => p.Contains("\"update_id\":100"));
        Assert.Contains(sent, p => p.Contains("\"update_id\":101"));
    }
}
