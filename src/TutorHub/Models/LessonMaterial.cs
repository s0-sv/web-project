namespace TutorHub.Models;

public class LessonMaterial : ITenantEntity
{
    public int Id { get; set; }

    public int TenantId { get; set; }

    public int LessonId { get; set; }
    public Lesson? Lesson { get; set; }

    public string Title { get; set; } = "";

    public string OriginalFileName { get; set; } = "";

    public string ContentType { get; set; } = "";

    public long SizeBytes { get; set; }

    public string StorageKey { get; set; } = "";

    public DateTimeOffset UploadedAt { get; set; }
}
