using System.Collections.Concurrent;
using Azure.Storage.Queues;
using Azure.Storage.Queues.Models;

namespace TutorHub.Services;

public record ReceivedMessage(string Id, string PopReceipt, string Body, long DequeueCount);

public interface IMessageQueue
{
    Task SendAsync(string queue, string payload, CancellationToken ct = default);

    Task<IReadOnlyList<ReceivedMessage>> ReceiveAsync(string queue, int maxMessages, TimeSpan visibilityTimeout, CancellationToken ct = default);

    Task DeleteAsync(string queue, ReceivedMessage message, CancellationToken ct = default);
}

public class AzureMessageQueue(string connectionString) : IMessageQueue
{
    public const string ConfigurationKey = "Storage:Queues:ConnectionString";

    private readonly QueueServiceClient _service = new(
        connectionString,
        new QueueClientOptions { MessageEncoding = QueueMessageEncoding.Base64 });

    private readonly ConcurrentDictionary<string, Lazy<Task<QueueClient>>> _queues = new();

    public async Task SendAsync(string queue, string payload, CancellationToken ct = default)
    {
        var client = await GetQueueAsync(queue, ct);
        await client.SendMessageAsync(payload, ct);
    }

    public async Task<IReadOnlyList<ReceivedMessage>> ReceiveAsync(
        string queue,
        int maxMessages,
        TimeSpan visibilityTimeout,
        CancellationToken ct = default)
    {
        var client = await GetQueueAsync(queue, ct);
        QueueMessage[] messages = await client.ReceiveMessagesAsync(maxMessages, visibilityTimeout, ct);
        return messages
            .Select(m => new ReceivedMessage(m.MessageId, m.PopReceipt, m.Body.ToString(), m.DequeueCount))
            .ToList();
    }

    public async Task DeleteAsync(string queue, ReceivedMessage message, CancellationToken ct = default)
    {
        var client = await GetQueueAsync(queue, ct);
        await client.DeleteMessageAsync(message.Id, message.PopReceipt, ct);
    }

    private async Task<QueueClient> GetQueueAsync(string queue, CancellationToken ct)
    {
        var lazy = _queues.GetOrAdd(queue, name => new Lazy<Task<QueueClient>>(async () =>
        {
            var client = _service.GetQueueClient(name);
            await client.CreateIfNotExistsAsync(cancellationToken: CancellationToken.None);
            return client;
        }));

        try
        {
            return await lazy.Value.WaitAsync(ct);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            _queues.TryRemove(new KeyValuePair<string, Lazy<Task<QueueClient>>>(queue, lazy));
            throw;
        }
    }
}
