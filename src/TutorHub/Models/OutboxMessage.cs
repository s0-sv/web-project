namespace TutorHub.Models;

public class OutboxMessage
{
    public long Id { get; set; }

    public string Queue { get; set; } = "";

    public string Payload { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset AvailableAt { get; set; }

    public int Attempts { get; set; }

    public DateTimeOffset? SentAt { get; set; }

    public string? LastError { get; set; }
}
