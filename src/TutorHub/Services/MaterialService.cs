using Microsoft.EntityFrameworkCore;
using TutorHub.Data;
using TutorHub.Models;

namespace TutorHub.Services;

public class MaterialService(AppDbContext db, IFileStorage storage, ILogger<MaterialService> logger)
{
    public async Task<LessonMaterial> AddAsync(int lessonId, string title, IFormFile file, CancellationToken ct = default)
    {
        var material = new LessonMaterial { LessonId = lessonId, Title = title };
        await StoreFileAsync(material, file, ct);
        db.LessonMaterials.Add(material);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            DeleteFile(material.StorageKey);
            throw;
        }

        return material;
    }

    public async Task UpdateAsync(LessonMaterial material, string title, IFormFile? newFile, CancellationToken ct = default)
    {
        material.Title = title;
        var previousKey = material.StorageKey;

        if (newFile is not null)
        {
            await StoreFileAsync(material, newFile, ct);
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            if (newFile is not null)
            {
                DeleteFile(material.StorageKey);
            }

            throw;
        }

        if (newFile is not null)
        {
            DeleteFile(previousKey);
        }
    }

    public async Task DeleteAsync(LessonMaterial material, CancellationToken ct = default)
    {
        db.LessonMaterials.Remove(material);
        await db.SaveChangesAsync(ct);
        DeleteFile(material.StorageKey);
    }

    public async Task DeleteLessonAsync(Lesson lesson, CancellationToken ct = default)
    {
        var keys = await db.LessonMaterials
            .Where(m => m.LessonId == lesson.Id)
            .Select(m => m.StorageKey)
            .ToListAsync(ct);

        db.Lessons.Remove(lesson);
        await db.SaveChangesAsync(ct);

        foreach (var key in keys)
        {
            DeleteFile(key);
        }
    }

    public Stream? OpenFile(LessonMaterial material) => storage.OpenRead(material.StorageKey);

    private async Task StoreFileAsync(LessonMaterial material, IFormFile file, CancellationToken ct)
    {
        var fileName = Path.GetFileName(file.FileName);
        await using var content = file.OpenReadStream();

        material.StorageKey = await storage.SaveAsync(content, Path.GetExtension(fileName), ct);
        material.OriginalFileName = fileName.Length <= 255 ? fileName : fileName[^255..];
        material.ContentType = MaterialFileRules.ContentTypeFor(fileName);
        material.SizeBytes = file.Length;
        material.UploadedAt = DateTimeOffset.UtcNow;
    }

    private void DeleteFile(string key)
    {
        try
        {
            storage.Delete(key);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(e, "Could not delete stored file {Key}", key);
        }
    }
}
