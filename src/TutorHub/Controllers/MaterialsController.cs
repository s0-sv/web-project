using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TutorHub.Data;
using TutorHub.Services;
using TutorHub.ViewModels;

namespace TutorHub.Controllers;

public class MaterialsController(AppDbContext db, MaterialService materials) : AppController
{
    public async Task<IActionResult> Index() =>
        View(await db.LessonMaterials.AsNoTracking()
            .Include(m => m.Lesson)
            .OrderByDescending(m => m.UploadedAt)
            .ToListAsync());

    public async Task<IActionResult> Download(int id)
    {
        var material = await db.LessonMaterials.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id);
        var stream = material is null ? null : materials.OpenFile(material);

        return stream is null
            ? NotFound()
            : File(stream, material!.ContentType, material.OriginalFileName);
    }

    public async Task<IActionResult> Create(int lessonId)
    {
        var topic = await db.Lessons.Where(l => l.Id == lessonId).Select(l => l.Topic).FirstOrDefaultAsync();
        return topic is null
            ? NotFound()
            : View(new MaterialFormModel { LessonId = lessonId, LessonTopic = topic });
    }

    [HttpPost]
    [RequestSizeLimit(MaterialFileRules.MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaterialFileRules.MaxRequestBytes)]
    public async Task<IActionResult> Create(MaterialFormModel input)
    {
        var topic = await db.Lessons.Where(l => l.Id == input.LessonId).Select(l => l.Topic).FirstOrDefaultAsync();
        if (topic is null)
        {
            return NotFound();
        }

        input.LessonTopic = topic;

        if (MaterialFileRules.Validate(input.File) is { } fileError)
        {
            ModelState.AddModelError(nameof(input.File), fileError);
        }

        if (!ModelState.IsValid)
        {
            return View(input);
        }

        await materials.AddAsync(input.LessonId, input.Title, input.File!);
        FlashSuccess("Матеріал додано.");
        return RedirectToAction("Details", "Lessons", new { id = input.LessonId });
    }

    public async Task<IActionResult> Edit(int id)
    {
        var material = await db.LessonMaterials.AsNoTracking()
            .Include(m => m.Lesson)
            .FirstOrDefaultAsync(m => m.Id == id);

        return material is null
            ? NotFound()
            : View(new MaterialFormModel
            {
                Id = material.Id,
                LessonId = material.LessonId,
                LessonTopic = material.Lesson!.Topic,
                CurrentFileName = material.OriginalFileName,
                Title = material.Title
            });
    }

    [HttpPost]
    [RequestSizeLimit(MaterialFileRules.MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaterialFileRules.MaxRequestBytes)]
    public async Task<IActionResult> Edit(int id, MaterialFormModel input)
    {
        var material = await db.LessonMaterials.Include(m => m.Lesson).FirstOrDefaultAsync(m => m.Id == id);
        if (material is null)
        {
            return NotFound();
        }

        input.Id = id;
        input.LessonId = material.LessonId;
        input.LessonTopic = material.Lesson!.Topic;
        input.CurrentFileName = material.OriginalFileName;

        if (input.File is not null && MaterialFileRules.Validate(input.File) is { } fileError)
        {
            ModelState.AddModelError(nameof(input.File), fileError);
        }

        if (!ModelState.IsValid)
        {
            return View(input);
        }

        await materials.UpdateAsync(material, input.Title, input.File);
        FlashSuccess("Зміни збережено.");
        return RedirectToAction("Details", "Lessons", new { id = material.LessonId });
    }

    public async Task<IActionResult> Delete(int id)
    {
        var material = await db.LessonMaterials.AsNoTracking()
            .Include(m => m.Lesson)
            .FirstOrDefaultAsync(m => m.Id == id);

        return material is null ? NotFound() : View(material);
    }

    [HttpPost, ActionName("Delete")]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var material = await db.LessonMaterials.FindAsync(id);
        if (material is null)
        {
            return NotFound();
        }

        await materials.DeleteAsync(material);
        FlashSuccess("Матеріал видалено.");
        return RedirectToAction("Details", "Lessons", new { id = material.LessonId });
    }
}
