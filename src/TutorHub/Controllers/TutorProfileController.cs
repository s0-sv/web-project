using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TutorHub.Data;
using TutorHub.Models;
using TutorHub.Services;

namespace TutorHub.Controllers;

public record ProfileSubject(string Name, string? Description);

public record TutorProfilePage(
    Tutor Tutor,
    string TenantName,
    IReadOnlyList<ProfileSubject> Subjects,
    int CompletedLessons,
    string? PhotoUrl);

public class TutorPublishing(AppDbContext db)
{
    public async Task SetPublishedAsync(Tutor tutor, bool published, CancellationToken ct = default)
    {
        if (published && tutor.Slug is null)
        {
            var slug = Tutor.GenerateSlug(tutor.FullName);
            if (slug.Length == 0)
            {
                slug = "tutor";
            }

            var taken = await db.Tutors.IgnoreQueryFilters().AnyAsync(t => t.Slug == slug, ct);
            tutor.Slug = taken ? $"{slug}-{tutor.Id}" : slug;
        }

        tutor.IsPublished = published;
        await db.SaveChangesAsync(ct);
    }
}

[AllowAnonymous]
public class TutorProfileController(AppDbContext db, IFileStorage storage) : AppController
{
    [HttpGet("/tutor/{slug}")]
    public async Task<IActionResult> Index(string slug)
    {
        var tutor = await db.Tutors.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(t => t.Slug == slug && t.IsPublished);

        if (tutor is null)
        {
            return NotFound();
        }

        var lessons = db.Lessons.IgnoreQueryFilters().AsNoTracking()
            .Where(l => l.TenantId == tutor.TenantId && l.TutorId == tutor.Id);

        var subjects = await lessons
            .Where(l => l.Status != LessonStatus.Cancelled)
            .Select(l => new { l.Subject!.Name, l.Subject.Description })
            .Distinct()
            .OrderBy(s => s.Name)
            .ToListAsync();

        var tenantName = await db.Tenants.AsNoTracking()
            .Where(t => t.Id == tutor.TenantId)
            .Select(t => t.Name)
            .FirstAsync();

        return View(new TutorProfilePage(
            tutor,
            tenantName,
            subjects.Select(s => new ProfileSubject(s.Name, s.Description)).ToList(),
            await lessons.CountAsync(l => l.Status == LessonStatus.Completed),
            tutor.PhotoKey is null ? null : storage.PublicUrl(tutor.PhotoKey)));
    }

    [HttpGet("/tutor/photo/{key}")]
    public async Task<IActionResult> Photo(string key)
    {
        var stream = await storage.OpenPublicReadAsync(key);
        if (stream is null)
        {
            return NotFound();
        }

        Response.Headers.CacheControl = "public, max-age=86400";
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(stream, PhotoRules.ContentTypeForKey(key));
    }
}
