using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TutorHub.Data;
using TutorHub.Models;

namespace TutorHub.Controllers;

[Authorize(Policy = AppPolicies.Staff)]
public class SubjectsController(AppDbContext db) : AppController
{
    public async Task<IActionResult> Index() =>
        View(await db.Subjects.AsNoTracking().Include(s => s.Lessons).OrderBy(s => s.Name).ToListAsync());

    public async Task<IActionResult> Details(int id)
    {
        var subject = await db.Subjects.AsNoTracking()
            .Include(s => s.Lessons).ThenInclude(l => l.Tutor)
            .Include(s => s.Lessons).ThenInclude(l => l.Student)
            .FirstOrDefaultAsync(s => s.Id == id);

        return subject is null ? NotFound() : View(subject);
    }

    public IActionResult Create() => View(new Subject());

    [HttpPost]
    public async Task<IActionResult> Create([Bind("Name,Description")] Subject input)
    {
        if (!ModelState.IsValid)
        {
            return View(input);
        }

        db.Subjects.Add(input);
        return await SaveAsync(input, "Предмет створено.") ?? View(input);
    }

    public async Task<IActionResult> Edit(int id)
    {
        var subject = await db.Subjects.FindAsync(id);
        return subject is null ? NotFound() : View(subject);
    }

    [HttpPost]
    public async Task<IActionResult> Edit(int id, [Bind("Name,Description")] Subject input)
    {
        input.Id = id;
        if (!ModelState.IsValid)
        {
            return View(input);
        }

        var subject = await db.Subjects.FindAsync(id);
        if (subject is null)
        {
            return NotFound();
        }

        subject.Name = input.Name;
        subject.Description = input.Description;
        return await SaveAsync(subject, "Зміни збережено.") ?? View(input);
    }

    public async Task<IActionResult> Delete(int id)
    {
        var subject = await db.Subjects.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id);
        return subject is null ? NotFound() : View(subject);
    }

    [HttpPost, ActionName("Delete")]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var subject = await db.Subjects.FindAsync(id);
        if (subject is null)
        {
            return NotFound();
        }

        db.Subjects.Remove(subject);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException e) when (e.IsForeignKeyViolation())
        {
            FlashError("Неможливо видалити предмет, поки за ним є заняття.");
            return RedirectToAction(nameof(Delete), new { id });
        }

        FlashSuccess("Предмет видалено.");
        return RedirectToAction(nameof(Index));
    }

    private async Task<IActionResult?> SaveAsync(Subject subject, string message)
    {
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException e) when (e.IsUniqueViolation())
        {
            ModelState.AddModelError(nameof(Subject.Name), "Предмет із такою назвою вже існує.");
            return null;
        }

        FlashSuccess(message);
        return RedirectToAction(nameof(Details), new { id = subject.Id });
    }
}
