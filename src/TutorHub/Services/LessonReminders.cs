using Microsoft.EntityFrameworkCore;
using TutorHub.Data;
using TutorHub.Integrations;
using TutorHub.Models;

namespace TutorHub.Services;

public class LessonReminders(AppDbContext db, TimeProvider time)
{
    public const int BatchSize = 100;

    public async Task<int> SendDueAsync(CancellationToken ct = default)
    {
        var now = time.GetUtcNow();
        var dayEdge = now + LessonNotificationInterceptor.DayBefore;
        var hourEdge = now + LessonNotificationInterceptor.HourBefore;

        var due = await db.Lessons.IgnoreQueryFilters().AsNoTracking()
            .Where(l => l.Status == LessonStatus.Scheduled
                && l.StartsAt > now
                && l.StartsAt <= dayEdge
                && l.Student!.TelegramChatId != null
                && (l.ReminderHourSentAt == null && l.StartsAt <= hourEdge || l.ReminderDaySentAt == null && l.StartsAt > hourEdge))
            .OrderBy(l => l.StartsAt)
            .Take(BatchSize)
            .Select(l => new
            {
                l.Id,
                l.StartsAt,
                ChatId = l.Student!.TelegramChatId!.Value,
                Info = new LessonInfo(l.Subject!.Name, l.Tutor!.FullName, l.Topic, l.StartsAt, l.DurationMinutes, l.MeetingUrl)
            })
            .ToListAsync(ct);

        var sent = 0;
        foreach (var lesson in due)
        {
            var hour = lesson.StartsAt <= hourEdge;
            await using var transaction = await db.Database.BeginTransactionAsync(ct);

            var claimed = hour
                ? await db.Lessons.IgnoreQueryFilters()
                    .Where(l => l.Id == lesson.Id && l.StartsAt == lesson.StartsAt
                        && l.Status == LessonStatus.Scheduled && l.ReminderHourSentAt == null)
                    .ExecuteUpdateAsync(s => s.SetProperty(l => l.ReminderHourSentAt, now), ct)
                : await db.Lessons.IgnoreQueryFilters()
                    .Where(l => l.Id == lesson.Id && l.StartsAt == lesson.StartsAt
                        && l.Status == LessonStatus.Scheduled && l.ReminderDaySentAt == null)
                    .ExecuteUpdateAsync(s => s.SetProperty(l => l.ReminderDaySentAt, now), ct);

            if (claimed == 1)
            {
                db.OutboxMessages.Add(Outbox.Create(
                    QueueNames.TelegramOutbox,
                    new TelegramOutgoingMessage(
                        lesson.ChatId,
                        hour ? TelegramTexts.ReminderHour(lesson.Info) : TelegramTexts.ReminderDay(lesson.Info)),
                    now));
                await db.SaveChangesAsync(ct);
                sent++;
            }

            await transaction.CommitAsync(ct);
        }

        return sent;
    }
}

public class LessonReminderService(IServiceScopeFactory scopes, ILogger<LessonReminderService> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                int sent;
                do
                {
                    await using var scope = scopes.CreateAsyncScope();
                    sent = await scope.ServiceProvider.GetRequiredService<LessonReminders>().SendDueAsync(stoppingToken);
                    if (sent > 0)
                    {
                        logger.LogInformation("Queued {Count} lesson reminders", sent);
                    }
                }
                while (sent == LessonReminders.BatchSize);
            }
            catch (Exception e) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(e, "Lesson reminders failed");
            }
        }
        while (await WaitAsync(timer, stoppingToken));
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try
        {
            return await timer.WaitForNextTickAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
