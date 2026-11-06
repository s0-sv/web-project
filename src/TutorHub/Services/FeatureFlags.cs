using Microsoft.EntityFrameworkCore;
using TutorHub.Contracts;
using TutorHub.Data;
using TutorHub.Models;

namespace TutorHub.Services;

public enum AppFeature
{
    Analytics
}

public class FeatureFlags(AppDbContext db, TenantContext tenant)
{
    private static readonly Dictionary<AppFeature, TenantPlan[]> PlansByFeature = new()
    {
        [AppFeature.Analytics] = [TenantPlan.Premium]
    };

    public static IReadOnlyList<AppFeature> FeaturesFor(TenantPlan plan) =>
        PlansByFeature.Where(p => p.Value.Contains(plan)).Select(p => p.Key).ToList();

    public async Task<bool> IsEnabledAsync(AppFeature feature, CancellationToken ct = default)
    {
        var plan = await db.Tenants.AsNoTracking()
            .Where(t => t.Id == tenant.TenantId)
            .Select(t => (TenantPlan?)t.Plan)
            .FirstOrDefaultAsync(ct);

        return plan is { } current && PlansByFeature[feature].Contains(current);
    }
}

public class LearningAnalytics(AppDbContext db)
{
    public async Task<AnalyticsDto> BuildAsync(CancellationToken ct = default)
    {
        var lessons = await db.Lessons.AsNoTracking()
            .Select(l => new
            {
                l.Status,
                l.DurationMinutes,
                Subject = l.Subject!.Name,
                Tutor = l.Tutor!.FullName,
                Rate = l.Tutor.HourlyRate
            })
            .ToListAsync(ct);

        var completed = lessons.Where(l => l.Status == LessonStatus.Completed).ToList();

        return new AnalyticsDto(
            lessons.Count,
            completed.Count,
            lessons.Count(l => l.Status == LessonStatus.Scheduled),
            lessons.Count(l => l.Status == LessonStatus.Cancelled),
            Math.Round(completed.Sum(l => l.DurationMinutes) / 60m, 2),
            Math.Round(completed.Sum(l => l.Rate * l.DurationMinutes / 60m), 2),
            completed.GroupBy(l => l.Subject)
                .Select(g => new SubjectAnalyticsDto(g.Key, g.Count(), Math.Round(g.Sum(l => l.DurationMinutes) / 60m, 2)))
                .OrderByDescending(s => s.Hours)
                .ToList(),
            completed.GroupBy(l => l.Tutor)
                .Select(g => new TutorAnalyticsDto(
                    g.Key,
                    g.Count(),
                    Math.Round(g.Sum(l => l.DurationMinutes) / 60m, 2),
                    Math.Round(g.Sum(l => l.Rate * l.DurationMinutes / 60m), 2)))
                .OrderByDescending(t => t.Revenue)
                .ToList());
    }
}
