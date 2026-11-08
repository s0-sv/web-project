using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TutorHub.Data;
using TutorHub.Integrations;
using TutorHub.Models;

namespace TutorHub.Services;

public record LessonInfo(string Subject, string Tutor, string Topic, DateTimeOffset StartsAt, int DurationMinutes, string? MeetingUrl);

public static class TelegramTexts
{
    public const string Help =
        "Я надсилаю сповіщення про заняття в TutorHub.\n\n" +
        "/lessons — найближчі заняття\n" +
        "/stop — вимкнути сповіщення\n" +
        "/help — ця довідка\n\n" +
        "Щоб підключити акаунт, відкрийте сторінку «Telegram» у TutorHub і перейдіть за посиланням.";

    public const string Welcome =
        "Вітаю! Щоб отримувати сповіщення, відкрийте сторінку «Telegram» у TutorHub і перейдіть за посиланням звідти.\n\n/help — довідка";

    public const string InvalidCode =
        "Посилання недійсне або прострочене. Отримайте нове на сторінці «Telegram» у TutorHub.";

    public const string NotLinked =
        "Цей чат ще не підключено до TutorHub. Відкрийте сторінку «Telegram» у TutorHub і перейдіть за посиланням.";

    public const string Stopped =
        "Сповіщення вимкнено. Щоб увімкнути знову, отримайте нове посилання на сторінці «Telegram» у TutorHub.";

    public const string NoLessons = "Найближчих занять немає.";

    public static string Linked(string student, string workspace) =>
        $"✅ Підключено: {student} ({workspace}).\nВи отримуватимете сповіщення про заняття й нагадування за добу та за годину.";

    public static string Created(LessonInfo lesson) => $"🗓 Нове заняття\n{Details(lesson)}";

    public static string Rescheduled(LessonInfo lesson, DateTimeOffset previous) =>
        $"🔄 Заняття перенесено\nБуло: {AppTime.Format(previous)}\n{Details(lesson)}";

    public static string Cancelled(LessonInfo lesson) =>
        $"❌ Заняття скасовано\n{lesson.Subject} з {lesson.Tutor}\n{AppTime.Format(lesson.StartsAt)}\nТема: {lesson.Topic}";

    public static string ReminderDay(LessonInfo lesson) => $"⏰ Нагадування: заняття через добу\n{Details(lesson)}";

    public static string ReminderHour(LessonInfo lesson) => $"⏰ Нагадування: заняття за годину\n{Details(lesson)}";

    public static string Upcoming(IEnumerable<(string Student, LessonInfo Lesson)> lessons, bool showStudent)
    {
        var lines = lessons.Select(l =>
            $"• {AppTime.Format(l.Lesson.StartsAt)} — {l.Lesson.Subject}, {l.Lesson.Tutor}" +
            (showStudent ? $" ({l.Student})" : "") +
            $"\n  {l.Lesson.Topic}");
        return "📚 Найближчі заняття\n" + string.Join("\n", lines);
    }

    private static string Details(LessonInfo lesson) =>
        $"{lesson.Subject} з {lesson.Tutor}\n" +
        $"{AppTime.Format(lesson.StartsAt)} (Київ), {lesson.DurationMinutes} хв\n" +
        $"Тема: {lesson.Topic}" +
        (string.IsNullOrWhiteSpace(lesson.MeetingUrl) ? "" : $"\nПосилання: {lesson.MeetingUrl}");
}

public class LessonNotificationInterceptor(TimeProvider time, OutboxSignal signal) : SaveChangesInterceptor
{
    public static readonly TimeSpan DayBefore = TimeSpan.FromHours(24);
    public static readonly TimeSpan HourBefore = TimeSpan.FromHours(1);

    private enum Kind
    {
        Created,
        Rescheduled,
        Cancelled
    }

    private record Snapshot(
        int TenantId,
        int StudentId,
        int TutorId,
        int SubjectId,
        string Topic,
        DateTimeOffset StartsAt,
        int DurationMinutes,
        string? MeetingUrl,
        LessonStatus Status)
    {
        public bool IsUpcoming(DateTimeOffset now) => Status == LessonStatus.Scheduled && StartsAt > now;
    }

    private record Pending(Kind Kind, Snapshot Lesson, DateTimeOffset? PreviousStart);

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is AppDbContext db)
        {
            EnqueueAsync(db, Collect(db), async: false, CancellationToken.None).GetAwaiter().GetResult();
        }

        return result;
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is AppDbContext db)
        {
            await EnqueueAsync(db, Collect(db), async: true, cancellationToken);
        }

        return result;
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        NotifyIfPending(eventData.Context);
        return result;
    }

    public override ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        NotifyIfPending(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void NotifyIfPending(DbContext? context)
    {
        if (context?.ChangeTracker.Entries<OutboxMessage>().Any(e => e.Entity.SentAt == null) == true)
        {
            signal.Notify();
        }
    }

    private List<Pending> Collect(AppDbContext db)
    {
        db.ChangeTracker.DetectChanges();
        var now = time.GetUtcNow();
        var pending = new List<Pending>();

        foreach (var entry in db.ChangeTracker.Entries<Lesson>().ToList())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
            {
                continue;
            }

            var before = entry.State == EntityState.Added ? null : Original(entry);
            var after = entry.State == EntityState.Deleted ? null : Current(entry.Entity);
            var wasUpcoming = before?.IsUpcoming(now) == true;
            var isUpcoming = after?.IsUpcoming(now) == true;
            var sameStudent = before is not null && after is not null
                && before.TenantId == after.TenantId && before.StudentId == after.StudentId;

            if (after is not null && isUpcoming && (!wasUpcoming || !sameStudent || before!.StartsAt != after.StartsAt))
            {
                var untilStart = after.StartsAt - now;
                entry.Entity.ReminderDaySentAt = untilStart <= DayBefore ? now : null;
                entry.Entity.ReminderHourSentAt = untilStart <= HourBefore ? now : null;
            }

            if (wasUpcoming && isUpcoming && sameStudent)
            {
                if (before!.StartsAt != after!.StartsAt)
                {
                    pending.Add(new Pending(Kind.Rescheduled, after, before.StartsAt));
                }

                continue;
            }

            if (wasUpcoming && (after is null || after.Status == LessonStatus.Cancelled || !sameStudent))
            {
                pending.Add(new Pending(Kind.Cancelled, before!, null));
            }

            if (isUpcoming)
            {
                pending.Add(new Pending(Kind.Created, after!, null));
            }
        }

        return pending;
    }

    private async Task EnqueueAsync(AppDbContext db, List<Pending> pending, bool async, CancellationToken ct)
    {
        if (pending.Count == 0)
        {
            return;
        }

        var studentIds = pending.Select(p => p.Lesson.StudentId).Distinct().ToList();
        var chats = (await List(db.Students.IgnoreQueryFilters().AsNoTracking()
                .Where(s => studentIds.Contains(s.Id) && s.TelegramChatId != null)
                .Select(s => new { s.Id, s.TenantId, ChatId = s.TelegramChatId!.Value }), async, ct))
            .ToDictionary(s => (s.TenantId, s.Id), s => s.ChatId);

        pending = pending.Where(p => chats.ContainsKey((p.Lesson.TenantId, p.Lesson.StudentId))).ToList();
        if (pending.Count == 0)
        {
            return;
        }

        var subjectIds = pending.Select(p => p.Lesson.SubjectId).Distinct().ToList();
        var tutorIds = pending.Select(p => p.Lesson.TutorId).Distinct().ToList();
        var subjects = (await List(db.Subjects.IgnoreQueryFilters().AsNoTracking()
                .Where(s => subjectIds.Contains(s.Id))
                .Select(s => new { s.Id, s.TenantId, s.Name }), async, ct))
            .ToDictionary(s => (s.TenantId, s.Id), s => s.Name);
        var tutors = (await List(db.Tutors.IgnoreQueryFilters().AsNoTracking()
                .Where(t => tutorIds.Contains(t.Id))
                .Select(t => new { t.Id, t.TenantId, t.FullName }), async, ct))
            .ToDictionary(t => (t.TenantId, t.Id), t => t.FullName);

        var now = time.GetUtcNow();
        foreach (var item in pending)
        {
            var lesson = item.Lesson;
            var info = new LessonInfo(
                subjects.GetValueOrDefault((lesson.TenantId, lesson.SubjectId), "Заняття"),
                tutors.GetValueOrDefault((lesson.TenantId, lesson.TutorId), "репетитором"),
                lesson.Topic,
                lesson.StartsAt,
                lesson.DurationMinutes,
                lesson.MeetingUrl);

            var text = item.Kind switch
            {
                Kind.Created => TelegramTexts.Created(info),
                Kind.Rescheduled => TelegramTexts.Rescheduled(info, item.PreviousStart!.Value),
                _ => TelegramTexts.Cancelled(info)
            };

            db.OutboxMessages.Add(Outbox.Create(
                QueueNames.TelegramOutbox,
                new TelegramOutgoingMessage(chats[(lesson.TenantId, lesson.StudentId)], text),
                now));
        }
    }

    private static async Task<List<T>> List<T>(IQueryable<T> query, bool async, CancellationToken ct) =>
        async ? await query.ToListAsync(ct) : query.ToList();

    private static Snapshot Original(EntityEntry<Lesson> entry)
    {
        var values = entry.OriginalValues;
        return new Snapshot(
            values.GetValue<int>(nameof(Lesson.TenantId)),
            values.GetValue<int>(nameof(Lesson.StudentId)),
            values.GetValue<int>(nameof(Lesson.TutorId)),
            values.GetValue<int>(nameof(Lesson.SubjectId)),
            values.GetValue<string>(nameof(Lesson.Topic)),
            values.GetValue<DateTimeOffset>(nameof(Lesson.StartsAt)),
            values.GetValue<int>(nameof(Lesson.DurationMinutes)),
            values.GetValue<string?>(nameof(Lesson.MeetingUrl)),
            values.GetValue<LessonStatus>(nameof(Lesson.Status)));
    }

    private static Snapshot Current(Lesson lesson) => new(
        lesson.TenantId,
        lesson.StudentId,
        lesson.TutorId,
        lesson.SubjectId,
        lesson.Topic,
        lesson.StartsAt,
        lesson.DurationMinutes,
        lesson.MeetingUrl,
        lesson.Status);
}
