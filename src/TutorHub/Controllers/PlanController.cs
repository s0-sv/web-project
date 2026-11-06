using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TutorHub.Data;
using TutorHub.Models;
using TutorHub.Services;

namespace TutorHub.Controllers;

[Authorize(Policy = AppPolicies.Staff)]
public class PlanController(AppDbContext db, TenantContext tenant) : AppController
{
    public async Task<IActionResult> Index() =>
        View(await db.Tenants.AsNoTracking().FirstAsync(t => t.Id == tenant.TenantId));

    [Authorize(Policy = AppPolicies.OwnerOnly)]
    [HttpPost]
    public async Task<IActionResult> Change(TenantPlan? plan)
    {
        if (!ModelState.IsValid || plan is null || !Enum.IsDefined(plan.Value))
        {
            return BadRequest();
        }

        var current = await db.Tenants.FirstAsync(t => t.Id == tenant.TenantId);
        current.Plan = plan.Value;
        await db.SaveChangesAsync();
        FlashSuccess($"План змінено на {plan}.");
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Policy = AppPolicies.OwnerOnly)]
    [HttpPost]
    public async Task<IActionResult> RegenerateInvite()
    {
        var current = await db.Tenants.FirstAsync(t => t.Id == tenant.TenantId);
        current.MemberInviteCode = Tenant.NewInviteCode();
        await db.SaveChangesAsync();
        FlashSuccess("Створено новий код запрошення. Старий більше не діє.");
        return RedirectToAction(nameof(Index));
    }
}
