using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using TutorHub.Data;
using TutorHub.Models;
using TutorHub.Services;
using TutorHub.ViewModels;

namespace TutorHub.Controllers;

public class LessonsController(AppDbContext db, MaterialService materials) : AppController
{
    public async Task<IActionResult> Index() =>
        View(await db.Lessons.AsNoTracking()
            .Include(l => l.Tutor)
            .Include(l => l.Student)
            .Include(l => l.Subject)
            .OrderByDescending(l => l.StartsAt)
            .ToListAsync());

    public async Task<IActionResult> Details(int id)
    {
        var lesson = await LoadFullAsync(id);
        return lesson is null ? NotFound() : View(lesson);
    }

    [Authorize(Policy = AppPolicies.Staff)]
    public async Task<IActionResult> Create()
    {
        await PopulateSelectsAsync();
        return View(new LessonFormModel());
    }

    [HttpPost]
    [Authorize(Policy = AppPolicies.Staff)]
    public async Task<IActionResult> Create(LessonFormModel input)
    {
        if (!ModelState.IsValid)
        {
            await PopulateSelectsAsync();
            return View(input);
        }

        var lesson = new Lesson();
        Apply(input, lesson);
        db.Lessons.Add(lesson);

        if (!await TrySaveAsync())
        {
            await PopulateSelectsAsync();
            return View(input);
        }

        FlashSuccess("Заняття створено.");
        return RedirectToAction(nameof(Details), new { id = lesson.Id });
    }

    [Authorize(Policy = AppPolicies.Staff)]
    public async Task<IActionResult> Edit(int id)
    {
        var lesson = await db.Lessons.AsNoTracking().FirstOrDefaultAsync(l => l.Id == id);
        if (lesson is null)
        {
            return NotFound();
        }

        await PopulateSelectsAsync();
        return View(new LessonFormModel
        {
            Id = lesson.Id,
            TutorId = lesson.TutorId,
            StudentId = lesson.StudentId,
            SubjectId = lesson.SubjectId,
            Topic = lesson.Topic,
            StartsAt = AppTime.ToLocal(lesson.StartsAt),
            DurationMinutes = lesson.DurationMinutes,
            MeetingUrl = lesson.MeetingUrl,
            Status = lesson.Status,
            Notes = lesson.Notes
        });
    }

    [HttpPost]
    [Authorize(Policy = AppPolicies.Staff)]
    public async Task<IActionResult> Edit(int id, LessonFormModel input)
    {
        input.Id = id;
        if (!ModelState.IsValid)
        {
            await PopulateSelectsAsync();
            return View(input);
        }

        var lesson = await db.Lessons.FindAsync(id);
        if (lesson is null)
        {
            return NotFound();
        }

        Apply(input, lesson);

        if (!await TrySaveAsync())
        {
            await PopulateSelectsAsync();
            return View(input);
        }

        FlashSuccess("Зміни збережено.");
        return RedirectToAction(nameof(Details), new { id });
    }

    [Authorize(Policy = AppPolicies.Staff)]
    public async Task<IActionResult> Delete(int id)
    {
        var lesson = await LoadFullAsync(id);
        return lesson is null ? NotFound() : View(lesson);
    }

    [HttpPost, ActionName("Delete")]
    [Authorize(Policy = AppPolicies.Staff)]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var lesson = await db.Lessons.FindAsync(id);
        if (lesson is null)
        {
            return NotFound();
        }

        await materials.DeleteLessonAsync(lesson);
        FlashSuccess("Заняття видалено.");
        return RedirectToAction(nameof(Index));
    }

    private Task<Lesson?> LoadFullAsync(int id) =>
        db.Lessons.AsNoTracking()
            .Include(l => l.Tutor)
            .Include(l => l.Student)
            .Include(l => l.Subject)
            .Include(l => l.Materials)
            .FirstOrDefaultAsync(l => l.Id == id);

    private static void Apply(LessonFormModel input, Lesson lesson)
    {
        lesson.TutorId = input.TutorId!.Value;
        lesson.StudentId = input.StudentId!.Value;
        lesson.SubjectId = input.SubjectId!.Value;
        lesson.Topic = input.Topic;
        lesson.StartsAt = AppTime.FromLocal(input.StartsAt!.Value);
        lesson.DurationMinutes = input.DurationMinutes;
        lesson.MeetingUrl = input.MeetingUrl;
        lesson.Status = input.Status;
        lesson.Notes = input.Notes;
    }

    private async Task<bool> TrySaveAsync()
    {
        try
        {
            await db.SaveChangesAsync();
            return true;
        }
        catch (DbUpdateException e) when (e.IsForeignKeyViolation())
        {
            ModelState.AddModelError("", "Обраного репетитора, учня або предмета більше не існує.");
            return false;
        }
    }

    private async Task PopulateSelectsAsync()
    {
        ViewBag.Tutors = new SelectList(
            await db.Tutors.AsNoTracking().OrderBy(t => t.FullName).ToListAsync(), nameof(Tutor.Id), nameof(Tutor.FullName));
        ViewBag.Students = new SelectList(
            await db.Students.AsNoTracking().OrderBy(s => s.FullName).ToListAsync(), nameof(Student.Id), nameof(Student.FullName));
        ViewBag.Subjects = new SelectList(
            await db.Subjects.AsNoTracking().OrderBy(s => s.Name).ToListAsync(), nameof(Subject.Id), nameof(Subject.Name));
    }
}
