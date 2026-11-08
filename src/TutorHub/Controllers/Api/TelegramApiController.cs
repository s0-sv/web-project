using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TutorHub.Models;
using TutorHub.Services;

namespace TutorHub.Controllers.Api;

[Route("api/telegram/link")]
[Authorize(Roles = TenantRoles.Student)]
public class TelegramApiController(TelegramLinks links, TenantContext tenant) : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<TelegramLinkStatus>> Get(CancellationToken ct) =>
        tenant.StudentId is { } id && await links.GetStatusAsync(id, ct) is { } status ? status : NotFound();

    [HttpPost]
    public async Task<ActionResult<TelegramLinkStatus>> Create(CancellationToken ct)
    {
        var status = tenant.StudentId is { } id ? await links.CreateLinkAsync(id, ct) : null;
        if (status is null)
        {
            return NotFound();
        }

        return status.LinkUrl is null
            ? Problem(detail: "Telegram bot is not configured or unavailable.", statusCode: StatusCodes.Status503ServiceUnavailable)
            : status;
    }

    [HttpDelete]
    public async Task<IActionResult> Delete(CancellationToken ct) =>
        tenant.StudentId is { } id && await links.UnlinkAsync(id, ct) ? NoContent() : NotFound();
}
