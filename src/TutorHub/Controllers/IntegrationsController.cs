using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TutorHub.Data;
using TutorHub.Integrations;
using TutorHub.Models;
using TutorHub.Services;
using TutorHub.ViewModels;

namespace TutorHub.Controllers;

[Authorize(Policy = AppPolicies.OwnerOnly)]
public class IntegrationsController(
    AppDbContext db,
    TenantContext tenant,
    FeatureFlags flags,
    HubSpotClient hubSpot,
    HubSpotTokens tokens,
    Outbox outbox,
    TimeProvider time,
    IServiceProvider services,
    ILogger<IntegrationsController> logger) : AppController
{
    public const int MaxTokenLength = 500;

    [HttpGet]
    public async Task<IActionResult> HubSpot(CancellationToken ct)
    {
        if (!await flags.IsEnabledAsync(AppFeature.CrmSync, ct))
        {
            return View("HubSpotUpgrade");
        }

        var current = await db.Tenants.AsNoTracking().FirstAsync(t => t.Id == tenant.TenantId, ct);
        return View(new HubSpotViewModel(
            current.HasHubSpot,
            current.HubSpotPortalId,
            current.HubSpotConnectedAt,
            current.HubSpotLastSyncAt,
            current.HubSpotLastError,
            await db.Students.CountAsync(ct),
            await db.Students.CountAsync(s => s.HubSpotContactId != null, ct),
            await db.Lessons.CountAsync(l => l.Status == LessonStatus.Completed, ct),
            await db.Lessons.CountAsync(l => l.HubSpotNoteId != null, ct),
            services.GetService<IMessageQueue>() is not null));
    }

    [HttpPost("Integrations/HubSpot/Connect")]
    public async Task<IActionResult> Connect(string? accessToken, CancellationToken ct)
    {
        if (!await flags.IsEnabledAsync(AppFeature.CrmSync, ct))
        {
            return RedirectToAction(nameof(HubSpot));
        }

        var token = accessToken?.Trim() ?? "";
        if (token.Length == 0 || token.Length > MaxTokenLength || token.Any(char.IsWhiteSpace))
        {
            FlashError("Вставте токен Private App з HubSpot (без пробілів).");
            return RedirectToAction(nameof(HubSpot));
        }

        long? portalId;
        try
        {
            portalId = await hubSpot.GetPortalIdAsync(token, ct);
        }
        catch (HubSpotApiException e) when (e.Status == System.Net.HttpStatusCode.Forbidden)
        {
            portalId = null;
        }
        catch (HubSpotApiException e) when (e.Status == System.Net.HttpStatusCode.Unauthorized)
        {
            FlashError("HubSpot не прийняв токен. Перевірте, що його скопійовано повністю.");
            return RedirectToAction(nameof(HubSpot));
        }
        catch (Exception e) when ((e is HubSpotApiException or HttpRequestException or TaskCanceledException) && !ct.IsCancellationRequested)
        {
            logger.LogWarning(e, "HubSpot token check failed");
            FlashError("HubSpot зараз недоступний. Спробуйте пізніше.");
            return RedirectToAction(nameof(HubSpot));
        }

        var current = await db.Tenants.FirstAsync(t => t.Id == tenant.TenantId, ct);
        current.HubSpotAccessToken = tokens.Protect(token);
        current.HubSpotPortalId = portalId;
        current.HubSpotConnectedAt = time.GetUtcNow();
        current.HubSpotLastError = null;
        await EnqueueFullSyncAsync(current.Id, ct);
        await db.SaveChangesAsync(ct);

        FlashSuccess("HubSpot підключено. Учні та проведені заняття синхронізуються у фоні.");
        return RedirectToAction(nameof(HubSpot));
    }

    [HttpPost("Integrations/HubSpot/Sync")]
    public async Task<IActionResult> Sync(CancellationToken ct)
    {
        var current = await db.Tenants.AsNoTracking().FirstAsync(t => t.Id == tenant.TenantId, ct);
        if (!current.HasHubSpot || !await flags.IsEnabledAsync(AppFeature.CrmSync, ct))
        {
            return RedirectToAction(nameof(HubSpot));
        }

        await EnqueueFullSyncAsync(current.Id, ct);
        await db.SaveChangesAsync(ct);
        FlashSuccess("Повну синхронізацію поставлено в чергу.");
        return RedirectToAction(nameof(HubSpot));
    }

    [HttpPost("Integrations/HubSpot/Disconnect")]
    public async Task<IActionResult> Disconnect(CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var current = await db.Tenants.FirstAsync(t => t.Id == tenant.TenantId, ct);
        current.HubSpotAccessToken = null;
        current.HubSpotPortalId = null;
        current.HubSpotConnectedAt = null;
        current.HubSpotLastSyncAt = null;
        current.HubSpotLastError = null;
        await db.SaveChangesAsync(ct);
        await db.Students.ExecuteUpdateAsync(s => s.SetProperty(x => x.HubSpotContactId, (string?)null), ct);
        await db.Lessons.ExecuteUpdateAsync(s => s.SetProperty(x => x.HubSpotNoteId, (string?)null), ct);
        await transaction.CommitAsync(ct);

        FlashSuccess("HubSpot відключено. Дані в HubSpot залишилися без змін.");
        return RedirectToAction(nameof(HubSpot));
    }

    private async Task EnqueueFullSyncAsync(int tenantId, CancellationToken ct)
    {
        var students = await db.Students.AsNoTracking().Select(s => new { s.Id, s.Email }).ToListAsync(ct);
        foreach (var student in students)
        {
            outbox.Add(QueueNames.CrmSync, new CrmSyncMessage(tenantId, student.Id, student.Email, null, Full: true));
        }
    }
}
