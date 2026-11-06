using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using TutorHub.Models;
using TutorHub.Services;

namespace TutorHub.Contracts;

public record SubjectDto(int Id, string Name, string? Description)
{
    public static readonly Expression<Func<Subject, SubjectDto>> Projection =
        s => new SubjectDto(s.Id, s.Name, s.Description);
}

public record SubjectRequest(
    [Required, StringLength(100)] string Name,
    [StringLength(1000)] string? Description);

public record TutorDto(
    int Id,
    string FullName,
    string Email,
    string? Phone,
    string? Bio,
    decimal HourlyRate,
    string? Headline,
    string? Slug,
    bool IsPublished)
{
    public static readonly Expression<Func<Tutor, TutorDto>> Projection =
        t => new TutorDto(t.Id, t.FullName, t.Email, t.Phone, t.Bio, t.HourlyRate, t.Headline, t.Slug, t.IsPublished);
}

public record TutorRequest(
    [Required, StringLength(150)] string FullName,
    [Required, EmailAddress, StringLength(254)] string Email,
    [Phone, StringLength(30)] string? Phone,
    [StringLength(2000)] string? Bio,
    [Required, Range(typeof(decimal), "0", "100000")] decimal? HourlyRate,
    [StringLength(200)] string? Headline = null,
    [StringLength(100), RegularExpression("^[a-z0-9]+(-[a-z0-9]+)*$")] string? Slug = null);

public record TutorPublicationRequest([Required] bool? IsPublished);

public record StudentDto(int Id, string FullName, string Email, string? Phone, string? LearningGoal)
{
    public static readonly Expression<Func<Student, StudentDto>> Projection =
        s => new StudentDto(s.Id, s.FullName, s.Email, s.Phone, s.LearningGoal);
}

public record StudentRequest(
    [Required, StringLength(150)] string FullName,
    [Required, EmailAddress, StringLength(254)] string Email,
    [Phone, StringLength(30)] string? Phone,
    [StringLength(1000)] string? LearningGoal);

public record LessonDto(
    int Id,
    int TutorId,
    string TutorName,
    int StudentId,
    string StudentName,
    int SubjectId,
    string SubjectName,
    string Topic,
    DateTimeOffset StartsAt,
    int DurationMinutes,
    string? MeetingUrl,
    LessonStatus Status,
    string? Notes,
    int MaterialsCount)
{
    public static readonly Expression<Func<Lesson, LessonDto>> Projection = l => new LessonDto(
        l.Id,
        l.TutorId,
        l.Tutor!.FullName,
        l.StudentId,
        l.Student!.FullName,
        l.SubjectId,
        l.Subject!.Name,
        l.Topic,
        l.StartsAt,
        l.DurationMinutes,
        l.MeetingUrl,
        l.Status,
        l.Notes,
        l.Materials.Count);
}

public record LessonRequest(
    [Required] int? TutorId,
    [Required] int? StudentId,
    [Required] int? SubjectId,
    [Required, StringLength(200)] string Topic,
    [Required] DateTimeOffset? StartsAt,
    [Range(15, 480)] int DurationMinutes,
    [Url, StringLength(500)] string? MeetingUrl,
    [EnumDataType(typeof(LessonStatus))] LessonStatus Status = LessonStatus.Scheduled,
    [StringLength(4000)] string? Notes = null);

public record MaterialDto(
    int Id,
    int LessonId,
    string Title,
    string FileName,
    string ContentType,
    long SizeBytes,
    DateTimeOffset UploadedAt,
    string FileUrl)
{
    public static readonly Expression<Func<LessonMaterial, MaterialDto>> Projection = m => new MaterialDto(
        m.Id,
        m.LessonId,
        m.Title,
        m.OriginalFileName,
        m.ContentType,
        m.SizeBytes,
        m.UploadedAt,
        "/api/materials/" + m.Id + "/file");
}

public record MaterialUpdateRequest([Required, StringLength(200)] string Title);

public record RegisterRequest(
    [Required, EmailAddress, StringLength(254)] string Email,
    [Required, StringLength(100, MinimumLength = 8)] string Password,
    [Required, StringLength(150)] string FullName,
    [StringLength(100)] string? WorkspaceName,
    [StringLength(64)] string? InviteCode);

public record TokenRequest([Required] string Email, [Required] string Password);

public record TokenResponse(string AccessToken, string TokenType, int ExpiresIn);

public record CurrentUserDto(
    string Id,
    string Email,
    string FullName,
    int TenantId,
    string TenantName,
    string Role,
    int? StudentId,
    TenantPlan Plan,
    IReadOnlyList<AppFeature> Features);

public record PlanChangeRequest([EnumDataType(typeof(TenantPlan))] TenantPlan Plan);

public record SubjectAnalyticsDto(string Subject, int Lessons, decimal Hours);

public record TutorAnalyticsDto(string Tutor, int Lessons, decimal Hours, decimal Revenue);

public record AnalyticsDto(
    int TotalLessons,
    int CompletedLessons,
    int ScheduledLessons,
    int CancelledLessons,
    decimal CompletedHours,
    decimal EstimatedRevenue,
    IReadOnlyList<SubjectAnalyticsDto> Subjects,
    IReadOnlyList<TutorAnalyticsDto> Tutors);

public record InviteCodeDto(string InviteCode);
