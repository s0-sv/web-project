using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TutorHub.Data;
using TutorHub.Models;

namespace TutorHub.Controllers;

[Authorize(Policy = AppPolicies.Staff)]
public class TutorsController(AppDbContext db) : AppController
{
    public async Task<IActionResult> Index() =>
        View(await db.Tutors.AsNoTracking().Include(t => t.Lessons).OrderBy(t => t.FullName).ToListAsync());

    public async Task<IActionResult> Details(int id)
    {
        var tutor = await db.Tutors.AsNoTracking()
            .Include(t => t.Lessons).ThenInclude(l => l.Student)
            .Include(t => t.Lessons).ThenInclude(l => l.Subject)
            .FirstOrDefaultAsync(t => t.Id == id);

        return tutor is null ? NotFound() : View(tutor);
    }

    public IActionResult Create() => View(new Tutor());

    [HttpPost]
    public async Task<IActionResult> Create([Bind("FullName,Email,Phone,Bio,HourlyRate")] Tutor input)
    {
        if (!ModelState.IsValid)
        {
            return View(input);
        }

        db.Tutors.Add(input);
        return await SaveAsync(input, "Репетитора додано.") ?? View(input);
    }

    public async Task<IActionResult> Edit(int id)
    {
        var tutor = await db.Tutors.FindAsync(id);
        return tutor is null ? NotFound() : View(tutor);
    }

    [HttpPost]
    public async Task<IActionResult> Edit(int id, [Bind("FullName,Email,Phone,Bio,HourlyRate")] Tutor input)
    {
        input.Id = id;
        if (!ModelState.IsValid)
        {
            return View(input);
        }

        var tutor = await db.Tutors.FindAsync(id);
        if (tutor is null)
        {
            return NotFound();
        }

        tutor.FullName = input.FullName;
        tutor.Email = input.Email;
        tutor.Phone = input.Phone;
        tutor.Bio = input.Bio;
        tutor.HourlyRate = input.HourlyRate;
        return await SaveAsync(tutor, "Зміни збережено.") ?? View(input);
    }

    public async Task<IActionResult> Delete(int id)
    {
        var tutor = await db.Tutors.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id);
        return tutor is null ? NotFound() : View(tutor);
    }

    [HttpPost, ActionName("Delete")]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var tutor = await db.Tutors.FindAsync(id);
        if (tutor is null)
        {
            return NotFound();
        }

        db.Tutors.Remove(tutor);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException e) when (e.IsForeignKeyViolation())
        {
            FlashError("Неможливо видалити репетитора, поки в нього є заняття.");
            return RedirectToAction(nameof(Delete), new { id });
        }

        FlashSuccess("Репетитора видалено.");
        return RedirectToAction(nameof(Index));
    }

    private async Task<IActionResult?> SaveAsync(Tutor tutor, string message)
    {
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException e) when (e.IsUniqueViolation())
        {
            ModelState.AddModelError(nameof(Tutor.Email), "Цей email уже використовується.");
            return null;
        }

        FlashSuccess(message);
        return RedirectToAction(nameof(Details), new { id = tutor.Id });
    }
}
