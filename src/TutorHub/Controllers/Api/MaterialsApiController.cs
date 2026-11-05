using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.EntityFrameworkCore;
using TutorHub.Contracts;
using TutorHub.Data;
using TutorHub.Services;

namespace TutorHub.Controllers.Api;

[Route("api")]
public class MaterialsApiController(AppDbContext db, MaterialService materials) : ApiControllerBase
{
    [HttpGet("materials")]
    [OutputCache(PolicyName = CacheInvalidationInterceptor.PolicyName)]
    public Task<PagedResult<MaterialDto>> GetAll([FromQuery] PageQuery page, CancellationToken ct) =>
        db.LessonMaterials.AsNoTracking()
            .OrderByDescending(m => m.UploadedAt).ThenBy(m => m.Id)
            .Select(MaterialDto.Projection)
            .ToPagedResultAsync(page, Request, ct);

    [HttpGet("lessons/{lessonId:int}/materials")]
    [OutputCache(PolicyName = CacheInvalidationInterceptor.PolicyName)]
    public async Task<ActionResult<PagedResult<MaterialDto>>> GetForLesson(
        int lessonId,
        [FromQuery] PageQuery page,
        CancellationToken ct)
    {
        if (!await db.Lessons.AnyAsync(l => l.Id == lessonId, ct))
        {
            return NotFound();
        }

        return await db.LessonMaterials.AsNoTracking()
            .Where(m => m.LessonId == lessonId)
            .OrderBy(m => m.Title).ThenBy(m => m.Id)
            .Select(MaterialDto.Projection)
            .ToPagedResultAsync(page, Request, ct);
    }

    [HttpGet("materials/{id:int}")]
    [OutputCache(PolicyName = CacheInvalidationInterceptor.PolicyName)]
    public async Task<ActionResult<MaterialDto>> GetById(int id, CancellationToken ct)
    {
        var dto = await LoadDtoAsync(id, ct);
        return dto is null ? NotFound() : dto;
    }

    [HttpGet("materials/{id:int}/file")]
    public async Task<IActionResult> Download(int id, CancellationToken ct)
    {
        var material = await db.LessonMaterials.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id, ct);
        var stream = material is null ? null : materials.OpenFile(material);

        return stream is null
            ? NotFound()
            : File(stream, material!.ContentType, material.OriginalFileName);
    }

    [HttpPost("lessons/{lessonId:int}/materials")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaterialFileRules.MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaterialFileRules.MaxRequestBytes)]
    public async Task<ActionResult<MaterialDto>> Upload(
        int lessonId,
        [FromForm, Required, StringLength(200)] string title,
        IFormFile file,
        CancellationToken ct)
    {
        if (!await db.Lessons.AnyAsync(l => l.Id == lessonId, ct))
        {
            return NotFound();
        }

        if (MaterialFileRules.Validate(file) is { } error)
        {
            return InvalidField(nameof(file), error);
        }

        var material = await materials.AddAsync(lessonId, title, file, ct);
        return CreatedAtAction(nameof(GetById), new { id = material.Id }, await LoadDtoAsync(material.Id, ct));
    }

    [HttpPut("materials/{id:int}")]
    public async Task<ActionResult<MaterialDto>> Update(int id, MaterialUpdateRequest request, CancellationToken ct)
    {
        var material = await db.LessonMaterials.FindAsync([id], ct);
        if (material is null)
        {
            return NotFound();
        }

        await materials.UpdateAsync(material, request.Title, null, ct);
        return (await LoadDtoAsync(id, ct))!;
    }

    [HttpPut("materials/{id:int}/file")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaterialFileRules.MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaterialFileRules.MaxRequestBytes)]
    public async Task<ActionResult<MaterialDto>> ReplaceFile(int id, IFormFile file, CancellationToken ct)
    {
        var material = await db.LessonMaterials.FindAsync([id], ct);
        if (material is null)
        {
            return NotFound();
        }

        if (MaterialFileRules.Validate(file) is { } error)
        {
            return InvalidField(nameof(file), error);
        }

        await materials.UpdateAsync(material, material.Title, file, ct);
        return (await LoadDtoAsync(id, ct))!;
    }

    [HttpDelete("materials/{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var material = await db.LessonMaterials.FindAsync([id], ct);
        if (material is null)
        {
            return NotFound();
        }

        await materials.DeleteAsync(material, ct);
        return NoContent();
    }

    private Task<MaterialDto?> LoadDtoAsync(int id, CancellationToken ct) =>
        db.LessonMaterials.AsNoTracking().Where(m => m.Id == id).Select(MaterialDto.Projection).FirstOrDefaultAsync(ct);
}
