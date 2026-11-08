using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TutorHub.Data;
using TutorHub.Integrations;
using TutorHub.Models;

namespace TutorHub.Services;

public record CrmSyncMessage(int TenantId, int? StudentId, string? Email, int? LessonId, bool Full);

public class HubSpotTokens(IDataProtectionProvider provider)
{
    private readonly IDataProtector _protector = provider.CreateProtector("TutorHub.HubSpot.AccessToken");

    public string Protect(string token) => _protector.Protect(token);

    public string? Unprotect(string protectedToken)
    {
        try
        {
            return _protector.Unprotect(protectedToken);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }
}

public class CrmSyncInterceptor(TimeProvider time) : SaveChangesInterceptor
{
    private static readonly string[] ContactFields = [nameof(Student.FullName), nameof(Student.Email), nameof(Student.Phone)];

    private static readonly string[] NoteFields =
    [
        nameof(Lesson.Status), nameof(Lesson.StartsAt), nameof(Lesson.DurationMinutes), nameof(Lesson.Topic),
        nameof(Lesson.SubjectId), nameof(Lesson.TutorId), nameof(Lesson.StudentId)
    ];

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is AppDbContext db)
        {
            EnqueueAsync(db, async: false, CancellationToken.None).GetAwaiter().GetResult();
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
            await EnqueueAsync(db, async: true, cancellationToken);
        }

        return result;
    }

    private async Task EnqueueAsync(AppDbContext db, bool async, CancellationToken ct)
    {
        db.ChangeTracker.DetectChanges();
        var messages = new List<CrmSyncMessage>();

        foreach (var entry in db.ChangeTracker.Entries<Student>())
        {
            if (entry.State == EntityState.Added
                || entry.State == EntityState.Modified && ContactFields.Any(f => entry.Property(f).IsModified))
            {
                messages.Add(new CrmSyncMessage(
                    entry.Entity.TenantId,
                    entry.State == EntityState.Added ? null : entry.Entity.Id,
                    entry.Entity.Email,
                    null,
                    false));
            }
        }

        foreach (var entry in db.ChangeTracker.Entries<Lesson>())
        {
            var lesson = entry.Entity;
            if (entry.State == EntityState.Added && lesson.Status == LessonStatus.Completed)
            {
                messages.Add(new CrmSyncMessage(lesson.TenantId, lesson.StudentId, null, null, false));
            }
            else if (entry.State == EntityState.Modified && NoteFields.Any(f => entry.Property(f).IsModified))
            {
                var wasCompleted = entry.OriginalValues.GetValue<LessonStatus>(nameof(Lesson.Status)) == LessonStatus.Completed;
                if (lesson.Status != LessonStatus.Completed && !wasCompleted && lesson.HubSpotNoteId is null)
                {
                    continue;
                }

                if (entry.Property(nameof(Lesson.StudentId)).IsModified)
                {
                    lesson.HubSpotNoteId = null;
                }

                messages.Add(new CrmSyncMessage(lesson.TenantId, lesson.StudentId, null, lesson.HubSpotNoteId is null ? null : lesson.Id, false));
            }
        }

        if (messages.Count == 0)
        {
            return;
        }

        var tenantIds = messages.Select(m => m.TenantId).Distinct().ToList();
        var query = db.Tenants.AsNoTracking()
            .Where(t => tenantIds.Contains(t.Id) && t.HubSpotAccessToken != null && t.Plan == TenantPlan.Premium)
            .Select(t => t.Id);
        var connected = (async ? await query.ToListAsync(ct) : query.ToList()).ToHashSet();

        var now = time.GetUtcNow();
        foreach (var message in messages.Where(m => connected.Contains(m.TenantId)).Distinct())
        {
            db.OutboxMessages.Add(Outbox.Create(QueueNames.CrmSync, message, now));
        }
    }
}

public class CrmSync(AppDbContext db, HubSpotClient hubSpot, HubSpotTokens tokens, TimeProvider time, ILogger<CrmSync> logger)
{
    public const int MaxErrorLength = 1000;

    private static readonly HtmlEncoder Html = HtmlEncoder.Create(UnicodeRanges.All);

    public static HubSpotContact ContactFor(Student student)
    {
        var parts = student.FullName.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return new HubSpotContact(student.Email, parts.ElementAtOrDefault(0) ?? "", parts.ElementAtOrDefault(1) ?? "", student.Phone);
    }

    public static string NoteBody(Lesson lesson)
    {
        var html = Html;
        return $"<p><strong>Заняття TutorHub: {html.Encode(lesson.Subject!.Name)}</strong></p>" +
            $"<p>{html.Encode(AppTime.Format(lesson.StartsAt))} (Київ), {lesson.DurationMinutes} хв, репетитор {html.Encode(lesson.Tutor!.FullName)}</p>" +
            $"<p>Тема: {html.Encode(lesson.Topic)}</p>" +
            $"<p>Статус: {html.Encode(lesson.Status.Label())}</p>";
    }

    public async Task SyncAsync(CrmSyncMessage message, CancellationToken ct = default)
    {
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == message.TenantId, ct);
        if (tenant?.HubSpotAccessToken is null || tenant.Plan != TenantPlan.Premium)
        {
            return;
        }

        var token = tokens.Unprotect(tenant.HubSpotAccessToken);
        if (token is null)
        {
            await FailAsync(tenant, "Не вдалося розшифрувати токен HubSpot. Підключіть HubSpot повторно.", ct);
            return;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var student = await LockStudentAsync(message, ct);
        if (student is null)
        {
            return;
        }

        try
        {
            var contactId = await EnsureContactAsync(token, student, ct);
            var lessons = await db.Lessons.IgnoreQueryFilters()
                .Include(l => l.Subject)
                .Include(l => l.Tutor)
                .Where(l => l.TenantId == tenant.Id && l.StudentId == student.Id
                    && (l.HubSpotNoteId == null && l.Status == LessonStatus.Completed
                        || l.HubSpotNoteId != null && (message.Full || l.Id == message.LessonId)))
                .OrderBy(l => l.StartsAt)
                .ToListAsync(ct);

            foreach (var lesson in lessons)
            {
                await SyncNoteAsync(token, contactId, lesson, ct);
            }

            tenant.HubSpotLastSyncAt = time.GetUtcNow();
            tenant.HubSpotLastError = null;
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (HubSpotApiException e) when (!e.IsRetryable)
        {
            logger.LogWarning("HubSpot sync for tenant {TenantId} failed: {Status} {Category}", tenant.Id, (int)e.Status, e.Category);
            tenant.HubSpotLastError = Truncate(e.IsAuthError
                ? "HubSpot відхилив токен. Перевірте права Private App або підключіть HubSpot повторно."
                : $"HubSpot не прийняв дані учня {student.Email}: {e.ApiMessage}");
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            try
            {
                await transaction.CommitAsync(CancellationToken.None);
            }
            catch (Exception commitError)
            {
                logger.LogWarning(commitError, "Could not keep partial HubSpot sync progress for tenant {TenantId}", tenant.Id);
            }

            throw;
        }
    }

    private async Task<Student?> LockStudentAsync(CrmSyncMessage message, CancellationToken ct)
    {
        if (message.StudentId is { } id)
        {
            return await db.Students
                .FromSql($"""SELECT * FROM "Students" WHERE "Id" = {id} AND "TenantId" = {message.TenantId} FOR UPDATE""")
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(ct);
        }

        var email = (message.Email ?? "").Trim().ToLowerInvariant();
        return await db.Students
            .FromSql($"""SELECT * FROM "Students" WHERE "Email" = {email} AND "TenantId" = {message.TenantId} FOR UPDATE""")
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(ct);
    }

    private async Task<string> EnsureContactAsync(string token, Student student, CancellationToken ct)
    {
        var contact = ContactFor(student);
        if (student.HubSpotContactId is { } existing)
        {
            try
            {
                await hubSpot.UpdateContactAsync(token, existing, contact, ct);
                return existing;
            }
            catch (HubSpotApiException e) when (e.Status == System.Net.HttpStatusCode.NotFound)
            {
                student.HubSpotContactId = null;
            }
        }

        student.HubSpotContactId = await hubSpot.UpsertContactAsync(token, contact, ct);
        await db.SaveChangesAsync(ct);
        return student.HubSpotContactId;
    }

    private async Task SyncNoteAsync(string token, string contactId, Lesson lesson, CancellationToken ct)
    {
        var body = NoteBody(lesson);
        if (lesson.HubSpotNoteId is { } noteId)
        {
            try
            {
                await hubSpot.UpdateNoteAsync(token, noteId, body, lesson.StartsAt, ct);
                return;
            }
            catch (HubSpotApiException e) when (e.Status == System.Net.HttpStatusCode.NotFound)
            {
                lesson.HubSpotNoteId = null;
            }
        }

        if (lesson.Status == LessonStatus.Completed)
        {
            lesson.HubSpotNoteId = await hubSpot.CreateNoteAsync(token, contactId, body, lesson.StartsAt, ct);
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task FailAsync(Tenant tenant, string error, CancellationToken ct)
    {
        tenant.HubSpotLastError = Truncate(error);
        await db.SaveChangesAsync(ct);
    }

    private static string Truncate(string error) => error.Length <= MaxErrorLength ? error : error[..MaxErrorLength];
}

public class CrmSyncConsumer(IServiceScopeFactory scopes, IMessageQueue queue, ILogger<CrmSyncConsumer> logger)
    : QueueConsumer(scopes, queue, logger)
{
    public override string Queue => QueueNames.CrmSync;

    protected override Task HandleAsync(IServiceProvider services, string body, CancellationToken ct) =>
        services.GetRequiredService<CrmSync>().SyncAsync(QueueJson.Deserialize<CrmSyncMessage>(body), ct);
}
