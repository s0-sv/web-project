using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using TutorHub.Contracts;
using TutorHub.Data;

namespace TutorHub.Services;

public enum LessonSummarySaveStatus
{
    Saved,
    Conflict,
    NotFound
}

public record LessonSummarySaveResult(LessonSummarySaveStatus Status, LessonSummaryDto? Summary = null);

public class LessonSummaryService(AppDbContext db)
{
    public const int MaxLength = 100_000;

    public async Task<LessonSummaryDto?> GetAsync(int lessonId, CancellationToken ct = default)
    {
        var lesson = await db.Lessons.AsNoTracking()
            .Where(l => l.Id == lessonId)
            .Select(l => new { l.Summary })
            .FirstOrDefaultAsync(ct);

        return lesson is null ? null : ToDto(lesson.Summary);
    }

    public async Task<LessonSummarySaveResult> SaveAsync(int lessonId, string markdown, string baseVersion, CancellationToken ct = default)
    {
        var text = Normalize(markdown);
        var lesson = await db.Lessons.AsNoTracking()
            .Where(l => l.Id == lessonId)
            .Select(l => new { l.Summary })
            .FirstOrDefaultAsync(ct);

        if (lesson is null)
        {
            return new LessonSummarySaveResult(LessonSummarySaveStatus.NotFound);
        }

        if (lesson.Summary == text)
        {
            return new LessonSummarySaveResult(LessonSummarySaveStatus.Saved, ToDto(text));
        }

        if (VersionOf(lesson.Summary) != baseVersion)
        {
            return new LessonSummarySaveResult(LessonSummarySaveStatus.Conflict, ToDto(lesson.Summary));
        }

        var updated = await db.Lessons
            .Where(l => l.Id == lessonId && l.Summary == lesson.Summary)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.Summary, text), ct);

        if (updated > 0)
        {
            return new LessonSummarySaveResult(LessonSummarySaveStatus.Saved, ToDto(text));
        }

        var latest = await GetAsync(lessonId, ct);
        return latest is null
            ? new LessonSummarySaveResult(LessonSummarySaveStatus.NotFound)
            : new LessonSummarySaveResult(LessonSummarySaveStatus.Conflict, latest);
    }

    private static LessonSummaryDto ToDto(string? summary) => new(summary ?? "", VersionOf(summary));

    private static string? Normalize(string markdown)
    {
        var text = markdown.Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd();
        return text.Length == 0 ? null : text;
    }

    private static string VersionOf(string? summary) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(summary ?? "")));
}
