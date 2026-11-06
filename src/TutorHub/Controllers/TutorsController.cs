using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TutorHub.Data;
using TutorHub.Models;
using TutorHub.Services;

namespace TutorHub.Controllers;

[Authorize(Policy = AppPolicies.Staff)]
public class TutorsController(AppDbContext db, IFileStorage storage, TutorPublishing publishing) : AppController
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
    public async Task<IActionResult> Create([Bind("FullName,Email,Phone,Bio,HourlyRate,Headline,Slug")] Tutor input)
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
    public async Task<IActionResult> Edit(int id, [Bind("FullName,Email,Phone,Bio,HourlyRate,Headline,Slug")] Tutor input)
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
        tutor.Headline = input.Headline;
        tutor.Slug = input.Slug ?? tutor.Slug;
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

        if (tutor.PhotoKey is not null)
        {
            await storage.DeletePublicAsync(tutor.PhotoKey);
        }

        FlashSuccess("Репетитора видалено.");
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Policy = AppPolicies.OwnerOnly)]
    [HttpPost]
    public async Task<IActionResult> Publish(int id, bool published)
    {
        var tutor = await db.Tutors.FindAsync(id);
        if (tutor is null)
        {
            return NotFound();
        }

        await publishing.SetPublishedAsync(tutor, published);
        FlashSuccess(published ? "Сторінку опубліковано. Контакти тепер видимі всім." : "Сторінку знято з публікації.");
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [RequestSizeLimit(PhotoRules.MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = PhotoRules.MaxRequestBytes)]
    public async Task<IActionResult> Photo(int id, IFormFile? photo)
    {
        var tutor = await db.Tutors.FindAsync(id);
        if (tutor is null)
        {
            return NotFound();
        }

        var (extension, contentType, error) = await PhotoRules.InspectAsync(photo);
        if (error is not null)
        {
            FlashError(error);
            return RedirectToAction(nameof(Details), new { id });
        }

        await using var content = photo!.OpenReadStream();
        var previous = tutor.PhotoKey;
        tutor.PhotoKey = await storage.SavePublicAsync(content, extension!, contentType!);
        await db.SaveChangesAsync();

        if (previous is not null)
        {
            await storage.DeletePublicAsync(previous);
        }

        FlashSuccess("Фото оновлено.");
        return RedirectToAction(nameof(Details), new { id });
    }

    private async Task<IActionResult?> SaveAsync(Tutor tutor, string message)
    {
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException e) when (e.ViolatesIndexOn(nameof(Tutor.Slug)))
        {
            ModelState.AddModelError(nameof(Tutor.Slug), "Ця адреса сторінки вже зайнята.");
            return null;
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
