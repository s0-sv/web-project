using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TutorHub.Data;
using TutorHub.Models;

namespace TutorHub.Controllers;

public class StudentsController(AppDbContext db) : AppController
{
    public async Task<IActionResult> Index() =>
        View(await db.Students.AsNoTracking().Include(s => s.Lessons).OrderBy(s => s.FullName).ToListAsync());

    public async Task<IActionResult> Details(int id)
    {
        var student = await db.Students.AsNoTracking()
            .Include(s => s.Lessons).ThenInclude(l => l.Tutor)
            .Include(s => s.Lessons).ThenInclude(l => l.Subject)
            .FirstOrDefaultAsync(s => s.Id == id);

        return student is null ? NotFound() : View(student);
    }

    public IActionResult Create() => View(new Student());

    [HttpPost]
    public async Task<IActionResult> Create([Bind("FullName,Email,Phone,LearningGoal")] Student input)
    {
        if (!ModelState.IsValid)
        {
            return View(input);
        }

        db.Students.Add(input);
        return await SaveAsync(input, "Учня додано.") ?? View(input);
    }

    public async Task<IActionResult> Edit(int id)
    {
        var student = await db.Students.FindAsync(id);
        return student is null ? NotFound() : View(student);
    }

    [HttpPost]
    public async Task<IActionResult> Edit(int id, [Bind("FullName,Email,Phone,LearningGoal")] Student input)
    {
        input.Id = id;
        if (!ModelState.IsValid)
        {
            return View(input);
        }

        var student = await db.Students.FindAsync(id);
        if (student is null)
        {
            return NotFound();
        }

        student.FullName = input.FullName;
        student.Email = input.Email;
        student.Phone = input.Phone;
        student.LearningGoal = input.LearningGoal;
        return await SaveAsync(student, "Зміни збережено.") ?? View(input);
    }

    public async Task<IActionResult> Delete(int id)
    {
        var student = await db.Students.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id);
        return student is null ? NotFound() : View(student);
    }

    [HttpPost, ActionName("Delete")]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var student = await db.Students.FindAsync(id);
        if (student is null)
        {
            return NotFound();
        }

        db.Students.Remove(student);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException e) when (e.IsForeignKeyViolation())
        {
            FlashError("Неможливо видалити учня, поки в нього є заняття.");
            return RedirectToAction(nameof(Delete), new { id });
        }

        FlashSuccess("Учня видалено.");
        return RedirectToAction(nameof(Index));
    }

    private async Task<IActionResult?> SaveAsync(Student student, string message)
    {
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException e) when (e.IsUniqueViolation())
        {
            ModelState.AddModelError(nameof(Student.Email), "Цей email уже використовується.");
            return null;
        }

        FlashSuccess(message);
        return RedirectToAction(nameof(Details), new { id = student.Id });
    }
}
