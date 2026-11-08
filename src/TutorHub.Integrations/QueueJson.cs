using System.Text.Json;

namespace TutorHub.Integrations;

public static class QueueJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web);

    public static string Serialize<T>(T message) => JsonSerializer.Serialize(message, Options);

    public static T Deserialize<T>(string payload) =>
        JsonSerializer.Deserialize<T>(payload, Options)
        ?? throw new JsonException($"Queue message is empty or null for {typeof(T).Name}.");
}
