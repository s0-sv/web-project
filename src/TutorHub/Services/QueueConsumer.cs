using TutorHub.Integrations;

namespace TutorHub.Services;

public abstract class QueueConsumer(IServiceScopeFactory scopes, IMessageQueue queue, ILogger logger) : BackgroundService
{
    public const int BatchSize = 16;

    public static readonly TimeSpan VisibilityTimeout = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan MinIdleInterval = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan MaxIdleInterval = TimeSpan.FromSeconds(15);

    public abstract string Queue { get; }

    protected abstract Task HandleAsync(IServiceProvider services, string body, CancellationToken ct);

    public async Task<int> ProcessBatchAsync(CancellationToken ct = default)
    {
        var messages = await queue.ReceiveAsync(Queue, BatchSize, VisibilityTimeout, ct);
        foreach (var message in messages)
        {
            if (message.DequeueCount > QueueNames.MaxDequeueCount)
            {
                logger.LogError("Message {Id} in {Queue} moved to poison queue after {Count} attempts",
                    message.Id, Queue, message.DequeueCount - 1);
                await queue.SendAsync(QueueNames.Poison(Queue), message.Body, ct);
                await queue.DeleteAsync(Queue, message, ct);
                continue;
            }

            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await HandleAsync(scope.ServiceProvider, message.Body, ct);
                await queue.DeleteAsync(Queue, message, ct);
            }
            catch (Exception e) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning(e, "Message {Id} in {Queue} failed, attempt {Count}", message.Id, Queue, message.DequeueCount);
            }
        }

        return messages.Count;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var idle = MinIdleInterval;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (await ProcessBatchAsync(stoppingToken) > 0)
                {
                    idle = MinIdleInterval;
                    continue;
                }
            }
            catch (Exception e) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(e, "Receiving from {Queue} failed", Queue);
            }

            try
            {
                await Task.Delay(idle, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            idle = TimeSpan.FromTicks(Math.Min(idle.Ticks * 2, MaxIdleInterval.Ticks));
        }
    }
}
