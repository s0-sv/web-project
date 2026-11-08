using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TutorHub.Models;
using TutorHub.Services;

namespace TutorHub.Controllers;

[Authorize(Roles = TenantRoles.Student)]
public class TelegramController(TelegramLinks links, TenantContext tenant) : AppController
{
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var status = tenant.StudentId is { } id ? await links.GetStatusAsync(id, ct) : null;
        return status is null ? NotFound() : View(status);
    }

    [HttpPost]
    public async Task<IActionResult> Link(CancellationToken ct)
    {
        var status = tenant.StudentId is { } id ? await links.CreateLinkAsync(id, ct) : null;
        if (status is null)
        {
            return NotFound();
        }

        if (status.LinkUrl is null)
        {
            FlashError("Telegram-бот зараз недоступний. Спробуйте пізніше.");
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Unlink(CancellationToken ct)
    {
        if (tenant.StudentId is not { } id || !await links.UnlinkAsync(id, ct))
        {
            return NotFound();
        }

        FlashSuccess("Telegram відключено. Сповіщення більше не надходитимуть.");
        return RedirectToAction(nameof(Index));
    }
}
