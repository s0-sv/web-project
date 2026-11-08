using System.Text;
using Microsoft.EntityFrameworkCore;
using TutorHub.Data;
using TutorHub.Integrations;
using TutorHub.Models;

namespace TutorHub.Services;

public class OutboxSignal
{
    private readonly SemaphoreSlim _signal = new(0, 1);

    public void Notify()
    {
        try
        {
            _signal.Release();
        }
        catch (SemaphoreFullException)
        {
        }
    }

    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken ct) => _signal.WaitAsync(timeout, ct);
}

public class Outbox(AppDbContext db, TimeProvider time, OutboxSignal signal)
{
    public const int MaxPayloadBytes = 48 * 1024;

    private bool _subscribed;

    public static OutboxMessage Create<T>(string queue, T message, DateTimeOffset now)
    {
        var payload = QueueJson.Serialize(message);
        if (Encoding.UTF8.GetByteCount(payload) > MaxPayloadBytes)
        {
            throw new InvalidOperationException($"Queue message for '{queue}' exceeds {MaxPayloadBytes} bytes.");
        }

        return new OutboxMessage
        {
            Queue = queue,
            Payload = payload,
            CreatedAt = now,
            AvailableAt = now
        };
    }

    public void Add<T>(string queue, T message)
    {
        db.OutboxMessages.Add(Create(queue, message, time.GetUtcNow()));

        if (!_subscribed)
        {
            _subscribed = true;
            db.SavedChanges += (_, _) => signal.Notify();
        }
    }
}

public class OutboxProcessor(AppDbContext db, IMessageQueue queue, TimeProvider time, ILogger<OutboxProcessor> logger)
{
    public const int BatchSize = 50;
    public const int MaxAttempts = 20;
    public const int MaxErrorLength = 1000;

    public static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan Retention = TimeSpan.FromDays(7);

    public static TimeSpan Backoff(int attempts) =>
        TimeSpan.FromSeconds(Math.Min(Math.Pow(2, attempts), MaxBackoff.TotalSeconds));

    public async Task<int> DispatchAsync(CancellationToken ct = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var now = time.GetUtcNow();
        var batch = await db.OutboxMessages
            .FromSql($"""
                SELECT * FROM "OutboxMessages"
                WHERE "SentAt" IS NULL AND "Attempts" < {MaxAttempts} AND "AvailableAt" <= {now}
                ORDER BY "Id"
                LIMIT {BatchSize}
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(ct);

        var sent = 0;
        foreach (var message in batch)
        {
            try
            {
                await queue.SendAsync(message.Queue, message.Payload, ct);
                message.SentAt = time.GetUtcNow();
                message.LastError = null;
                sent++;
            }
            catch (Exception e) when (!ct.IsCancellationRequested)
            {
                message.Attempts++;
                message.AvailableAt = time.GetUtcNow() + Backoff(message.Attempts);
                message.LastError = e.Message.Length <= MaxErrorLength ? e.Message : e.Message[..MaxErrorLength];
                logger.LogWarning(e, "Outbox message {Id} for queue {Queue} failed, attempt {Attempts}",
                    message.Id, message.Queue, message.Attempts);
            }
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return sent;
    }

    public Task<int> PurgeSentAsync(CancellationToken ct = default)
    {
        var threshold = time.GetUtcNow() - Retention;
        return db.OutboxMessages
            .Where(m => m.SentAt != null && m.SentAt < threshold)
            .ExecuteDeleteAsync(ct);
    }
}

public class OutboxDispatcher(
    IServiceScopeFactory scopes,
    OutboxSignal signal,
    TimeProvider time,
    ILogger<OutboxDispatcher> logger) : BackgroundService
{
    public static readonly TimeSpan IdleInterval = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan ErrorInterval = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan PurgeInterval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var nextPurge = time.GetUtcNow();
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = IdleInterval;
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var processor = scope.ServiceProvider.GetRequiredService<OutboxProcessor>();
                if (await processor.DispatchAsync(stoppingToken) == OutboxProcessor.BatchSize)
                {
                    delay = TimeSpan.Zero;
                }

                if (time.GetUtcNow() >= nextPurge)
                {
                    await processor.PurgeSentAsync(stoppingToken);
                    nextPurge = time.GetUtcNow() + PurgeInterval;
                }
            }
            catch (Exception e) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(e, "Outbox dispatch failed");
                delay = ErrorInterval;
            }

            if (delay > TimeSpan.Zero)
            {
                try
                {
                    await signal.WaitAsync(delay, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }
}
