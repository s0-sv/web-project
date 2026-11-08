using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TutorHub.Data;
using TutorHub.Integrations;
using TutorHub.Models;
using TutorHub.Services;
using Xunit;

namespace TutorHub.AuthTests;

public sealed class QueueFactAttribute : FactAttribute
{
    public const string Variable = "TUTORHUB_TEST_QUEUE";

    public QueueFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(Variable)))
        {
            Skip = $"Set {Variable} to an Azure Storage or Azurite connection string.";
        }
    }
}

public class ManualTime(DateTimeOffset now) : TimeProvider
{
    public static ManualTime StartingNow()
    {
        var now = DateTimeOffset.UtcNow;
        return new ManualTime(now.AddTicks(-(now.Ticks % TimeSpan.TicksPerMillisecond)));
    }

    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}

public class FakeMessageQueue : IMessageQueue
{
    private readonly ConcurrentDictionary<string, ConcurrentQueue<ReceivedMessage>> _queues = new();
    private int _ids;

    public ConcurrentBag<(string Queue, string Payload)> Sent { get; } = [];

    public ConcurrentBag<string> Deleted { get; } = [];

    public HashSet<string> FailingQueues { get; } = [];

    public TimeSpan SendDelay { get; set; }

    public async Task SendAsync(string queue, string payload, CancellationToken ct = default)
    {
        if (SendDelay > TimeSpan.Zero)
        {
            await Task.Delay(SendDelay, ct);
        }

        if (FailingQueues.Contains(queue))
        {
            throw new InvalidOperationException($"Queue {queue} is unavailable");
        }

        Sent.Add((queue, payload));
    }

    public void Seed(string queue, string body, long dequeueCount) =>
        _queues.GetOrAdd(queue, _ => new()).Enqueue(
            new ReceivedMessage($"m{Interlocked.Increment(ref _ids)}", "pop", body, dequeueCount));

    public Task<IReadOnlyList<ReceivedMessage>> ReceiveAsync(string queue, int maxMessages, TimeSpan visibilityTimeout, CancellationToken ct = default)
    {
        var result = new List<ReceivedMessage>();
        var pending = _queues.GetOrAdd(queue, _ => new());
        while (result.Count < maxMessages && pending.TryDequeue(out var message))
        {
            result.Add(message);
        }

        return Task.FromResult<IReadOnlyList<ReceivedMessage>>(result);
    }

    public Task DeleteAsync(string queue, ReceivedMessage message, CancellationToken ct = default)
    {
        Deleted.Add(message.Id);
        return Task.CompletedTask;
    }
}

public class RecordingConsumer(IServiceScopeFactory scopes, IMessageQueue queue)
    : QueueConsumer(scopes, queue, NullLogger.Instance)
{
    public override string Queue => "test-consumer";

    public ConcurrentBag<string> Handled { get; } = [];

    protected override Task HandleAsync(IServiceProvider services, string body, CancellationToken ct)
    {
        Assert.NotNull(services.GetRequiredService<AppDbContext>());
        if (body == "fail")
        {
            throw new InvalidOperationException("handler failed");
        }

        Handled.Add(body);
        return Task.CompletedTask;
    }
}

[Collection(AuthCollection.Name)]
public class MessagingTests(AuthTestFactory factory)
{
    private record Ping(string Text);

    private static string UniqueQueue() => AuthTestFactory.Unique("q")[..30];

    private OutboxProcessor Processor(AsyncServiceScope scope, IMessageQueue queue, TimeProvider time) =>
        new(scope.ServiceProvider.GetRequiredService<AppDbContext>(), queue, time, NullLogger<OutboxProcessor>.Instance);

    private async Task<List<OutboxMessage>> MessagesAsync(string queue)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().OutboxMessages.AsNoTracking()
            .Where(m => m.Queue == queue)
            .OrderBy(m => m.Id)
            .ToListAsync();
    }

    private async Task AddAsync(string queue, params string[] texts)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var outbox = scope.ServiceProvider.GetRequiredService<Outbox>();
        foreach (var text in texts)
        {
            outbox.Add(queue, new Ping(text));
        }

        await scope.ServiceProvider.GetRequiredService<AppDbContext>().SaveChangesAsync();
    }

    [Fact]
    public async Task Outbox_message_is_stored_only_when_the_unit_of_work_is_saved()
    {
        var queue = UniqueQueue();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<Outbox>().Add(queue, new Ping("lost"));
        }

        Assert.Empty(await MessagesAsync(queue));

        await AddAsync(queue, "kept");

        var message = Assert.Single(await MessagesAsync(queue));
        Assert.Equal("""{"text":"kept"}""", message.Payload);
        Assert.Null(message.SentAt);
        Assert.Equal(0, message.Attempts);
        Assert.Equal(message.CreatedAt, message.AvailableAt);
    }

    [Fact]
    public async Task Saving_outbox_message_wakes_the_dispatcher()
    {
        var signal = factory.Services.GetRequiredService<OutboxSignal>();
        while (await signal.WaitAsync(TimeSpan.Zero, CancellationToken.None))
        {
        }

        await AddAsync(UniqueQueue(), "wake");

        Assert.True(await signal.WaitAsync(TimeSpan.FromSeconds(1), CancellationToken.None));
    }

    [Fact]
    public async Task Oversized_payload_is_rejected()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var outbox = scope.ServiceProvider.GetRequiredService<Outbox>();

        var error = Assert.Throws<InvalidOperationException>(() =>
            outbox.Add(UniqueQueue(), new Ping(new string('я', Outbox.MaxPayloadBytes / 2))));

        Assert.Contains("exceeds", error.Message);
        Assert.Empty(scope.ServiceProvider.GetRequiredService<AppDbContext>().ChangeTracker.Entries<OutboxMessage>());
    }

    [Fact]
    public async Task Processor_sends_pending_messages_once_in_order()
    {
        var queueName = UniqueQueue();
        await AddAsync(queueName, "one", "two");
        var queue = new FakeMessageQueue();
        var time = ManualTime.StartingNow();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            Assert.True(await Processor(scope, queue, time).DispatchAsync() >= 2);
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            await Processor(scope, queue, time).DispatchAsync();
        }

        Assert.Equal(["""{"text":"one"}""", """{"text":"two"}"""],
            queue.Sent.Where(s => s.Queue == queueName).Select(s => s.Payload).Order().ToArray());
        Assert.All(await MessagesAsync(queueName), m =>
        {
            Assert.Equal(time.Now, m.SentAt);
            Assert.Null(m.LastError);
        });
    }

    [Fact]
    public async Task Failed_send_is_retried_with_backoff_and_then_given_up()
    {
        var queueName = UniqueQueue();
        await AddAsync(queueName, "retry");
        var queue = new FakeMessageQueue { FailingQueues = { queueName } };
        var time = ManualTime.StartingNow();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            await Processor(scope, queue, time).DispatchAsync();
        }

        var failed = Assert.Single(await MessagesAsync(queueName));
        Assert.Equal(1, failed.Attempts);
        Assert.Null(failed.SentAt);
        Assert.Equal(time.Now + TimeSpan.FromSeconds(2), failed.AvailableAt);
        Assert.Contains("unavailable", failed.LastError);

        time.Now += TimeSpan.FromSeconds(1);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            await Processor(scope, queue, time).DispatchAsync();
        }

        Assert.Equal(1, Assert.Single(await MessagesAsync(queueName)).Attempts);

        for (var attempt = 2; attempt <= OutboxProcessor.MaxAttempts + 2; attempt++)
        {
            time.Now += OutboxProcessor.MaxBackoff;
            await using var scope = factory.Services.CreateAsyncScope();
            await Processor(scope, queue, time).DispatchAsync();
        }

        Assert.Equal(OutboxProcessor.MaxAttempts, Assert.Single(await MessagesAsync(queueName)).Attempts);

        queue.FailingQueues.Clear();
        time.Now += OutboxProcessor.MaxBackoff;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            await Processor(scope, queue, time).DispatchAsync();
        }

        Assert.DoesNotContain(queue.Sent, s => s.Queue == queueName);
    }

    [Fact]
    public async Task Failure_in_one_queue_does_not_block_another()
    {
        var broken = UniqueQueue();
        var healthy = UniqueQueue();
        await AddAsync(broken, "a");
        await AddAsync(healthy, "b");
        var queue = new FakeMessageQueue { FailingQueues = { broken } };

        await using var scope = factory.Services.CreateAsyncScope();
        await Processor(scope, queue, ManualTime.StartingNow()).DispatchAsync();

        Assert.NotNull(Assert.Single(await MessagesAsync(healthy)).SentAt);
        Assert.Null(Assert.Single(await MessagesAsync(broken)).SentAt);
    }

    [Fact]
    public async Task Parallel_processors_never_send_the_same_message_twice()
    {
        var queueName = UniqueQueue();
        await AddAsync(queueName, Enumerable.Range(0, 30).Select(i => $"m{i}").ToArray());
        var queue = new FakeMessageQueue { SendDelay = TimeSpan.FromMilliseconds(10) };
        var time = ManualTime.StartingNow();

        await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
        {
            for (var round = 0; round < 3; round++)
            {
                await using var scope = factory.Services.CreateAsyncScope();
                await Processor(scope, queue, time).DispatchAsync();
            }
        }));

        var sent = queue.Sent.Where(s => s.Queue == queueName).Select(s => s.Payload).ToList();
        Assert.Equal(30, sent.Count);
        Assert.Equal(30, sent.Distinct().Count());
        Assert.All(await MessagesAsync(queueName), m => Assert.NotNull(m.SentAt));
    }

    [Fact]
    public async Task Purge_removes_only_old_sent_messages()
    {
        var queueName = UniqueQueue();
        await AddAsync(queueName, "old", "pending");
        var time = ManualTime.StartingNow();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var old = await db.OutboxMessages.SingleAsync(m => m.Queue == queueName && m.Payload.Contains("old"));
            old.SentAt = time.Now - OutboxProcessor.Retention - TimeSpan.FromMinutes(1);
            await db.SaveChangesAsync();

            await Processor(scope, new FakeMessageQueue(), time).PurgeSentAsync();
        }

        var remaining = Assert.Single(await MessagesAsync(queueName));
        Assert.Contains("pending", remaining.Payload);
    }

    [Fact]
    public async Task Hosted_dispatcher_delivers_saved_message_without_waiting_for_the_idle_interval()
    {
        var queueName = UniqueQueue();
        var dispatcher = new OutboxDispatcher(
            factory.Services.GetRequiredService<IServiceScopeFactory>(),
            factory.Services.GetRequiredService<OutboxSignal>(),
            TimeProvider.System,
            NullLogger<OutboxDispatcher>.Instance);

        await dispatcher.StartAsync(CancellationToken.None);
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(300));
            await AddAsync(queueName, "fast");

            var deadline = DateTime.UtcNow + OutboxDispatcher.IdleInterval / 2;
            while (!factory.Queue.Sent.Any(s => s.Queue == queueName) && DateTime.UtcNow < deadline)
            {
                await Task.Delay(50);
            }

            Assert.Contains(factory.Queue.Sent, s => s.Queue == queueName && s.Payload.Contains("fast"));
        }
        finally
        {
            await dispatcher.StopAsync(CancellationToken.None);
        }
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(4, 16)]
    [InlineData(8, 256)]
    [InlineData(9, 300)]
    [InlineData(19, 300)]
    public void Backoff_doubles_up_to_the_cap(int attempts, int seconds) =>
        Assert.Equal(TimeSpan.FromSeconds(seconds), OutboxProcessor.Backoff(attempts));

    [Fact]
    public async Task Consumer_deletes_handled_messages_and_keeps_failed_ones()
    {
        var queue = new FakeMessageQueue();
        var consumer = new RecordingConsumer(factory.Services.GetRequiredService<IServiceScopeFactory>(), queue);
        queue.Seed(consumer.Queue, "ok", 1);
        queue.Seed(consumer.Queue, "fail", 1);

        Assert.Equal(2, await consumer.ProcessBatchAsync());

        Assert.Equal(["ok"], consumer.Handled);
        Assert.Equal(["m1"], queue.Deleted);
        Assert.Empty(queue.Sent);
    }

    [Fact]
    public async Task Consumer_moves_exhausted_message_to_poison_queue_without_handling()
    {
        var queue = new FakeMessageQueue();
        var consumer = new RecordingConsumer(factory.Services.GetRequiredService<IServiceScopeFactory>(), queue);
        queue.Seed(consumer.Queue, "last-try", QueueNames.MaxDequeueCount);
        queue.Seed(consumer.Queue, "poison", QueueNames.MaxDequeueCount + 1);

        await consumer.ProcessBatchAsync();

        Assert.Equal(["last-try"], consumer.Handled);
        Assert.Equal((QueueNames.Poison(consumer.Queue), "poison"), Assert.Single(queue.Sent));
        Assert.Equal(["m1", "m2"], queue.Deleted.Order().ToArray());
    }

    [Fact]
    public void Poison_queue_names_follow_azure_functions_convention()
    {
        Assert.Equal("telegram-outbox-poison", QueueNames.Poison(QueueNames.TelegramOutbox));
        Assert.All(QueueNames.All, q => Assert.Matches("^[a-z0-9]+(-[a-z0-9]+)*$", QueueNames.Poison(q)));
    }

    [QueueFact]
    public async Task Azure_queue_messages_are_base64_encoded_for_azure_functions()
    {
        var connection = Environment.GetEnvironmentVariable(QueueFactAttribute.Variable)!;
        var name = UniqueQueue();
        var payload = QueueJson.Serialize(new Ping("Конспект"));

        await new AzureMessageQueue(connection).SendAsync(name, payload);

        var raw = new Azure.Storage.Queues.QueueClient(connection, name);
        var peeked = Assert.Single((await raw.PeekMessagesAsync(10)).Value);
        Assert.Equal(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(payload)), peeked.Body.ToString());
    }

    [QueueFact]
    public async Task Azure_queue_round_trip_preserves_unicode_payload()
    {
        var queue = new AzureMessageQueue(Environment.GetEnvironmentVariable(QueueFactAttribute.Variable)!);
        var name = UniqueQueue();
        var payload = QueueJson.Serialize(new Ping("Привіт, учню! ✓"));

        await queue.SendAsync(name, payload);
        var received = Assert.Single(await queue.ReceiveAsync(name, 10, TimeSpan.FromSeconds(1)));

        Assert.Equal(payload, received.Body);
        Assert.Equal(1, received.DequeueCount);
        Assert.Equal("Привіт, учню! ✓", QueueJson.Deserialize<Ping>(received.Body).Text);

        await Task.Delay(TimeSpan.FromSeconds(1.5));
        var again = Assert.Single(await queue.ReceiveAsync(name, 10, TimeSpan.FromSeconds(30)));
        Assert.Equal(2, again.DequeueCount);

        await queue.DeleteAsync(name, again);
        await Task.Delay(TimeSpan.FromSeconds(0.5));
        Assert.Empty(await queue.ReceiveAsync(name, 10, TimeSpan.FromSeconds(1)));
    }
}
