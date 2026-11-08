namespace TutorHub.Models;

public enum LessonStatus
{
    Scheduled,
    Completed,
    Cancelled
}

public static class LessonStatusExtensions
{
    public static string Label(this LessonStatus status) => status switch
    {
        LessonStatus.Scheduled => "Заплановано",
        LessonStatus.Completed => "Проведено",
        LessonStatus.Cancelled => "Скасовано",
        _ => status.ToString()
    };

    public static string BadgeClass(this LessonStatus status) => status switch
    {
        LessonStatus.Scheduled => "text-bg-primary",
        LessonStatus.Completed => "text-bg-success",
        LessonStatus.Cancelled => "text-bg-secondary",
        _ => "text-bg-light"
    };
}

public class Lesson : ITenantEntity
{
    public int Id { get; set; }

    public int TenantId { get; set; }

    public int TutorId { get; set; }
    public Tutor? Tutor { get; set; }

    public int StudentId { get; set; }
    public Student? Student { get; set; }

    public int SubjectId { get; set; }
    public Subject? Subject { get; set; }

    public string Topic { get; set; } = "";

    public DateTimeOffset StartsAt { get; set; }

    public int DurationMinutes { get; set; }

    public string? MeetingUrl { get; set; }

    public LessonStatus Status { get; set; } = LessonStatus.Scheduled;

    public string? Notes { get; set; }

    public string? Summary { get; set; }

    public DateTimeOffset? ReminderDaySentAt { get; set; }

    public DateTimeOffset? ReminderHourSentAt { get; set; }

    public List<LessonMaterial> Materials { get; set; } = [];
}
