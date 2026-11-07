using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.EntityFrameworkCore;
using TutorHub.Contracts;
using TutorHub.Data;
using TutorHub.Models;
using TutorHub.Services;

namespace TutorHub.Controllers.Api;

[Route("api/lessons")]
public class LessonsApiController(AppDbContext db, MaterialService materials, LessonSummaryService summaries) : ApiControllerBase
{
    [HttpGet]
    [OutputCache(PolicyName = CacheInvalidationInterceptor.PolicyName)]
    public Task<PagedResult<LessonDto>> GetAll([FromQuery] PageQuery page, CancellationToken ct) =>
        db.Lessons.AsNoTracking()
            .OrderByDescending(l => l.StartsAt).ThenBy(l => l.Id)
            .Select(LessonDto.Projection)
            .ToPagedResultAsync(page, Request, ct);

    [HttpGet("{id:int}")]
    [OutputCache(PolicyName = CacheInvalidationInterceptor.PolicyName)]
    public async Task<ActionResult<LessonDto>> GetById(int id, CancellationToken ct)
    {
        var dto = await LoadDtoAsync(id, ct);
        return dto is null ? NotFound() : dto;
    }

    [HttpPost]
    [Authorize(Policy = AppPolicies.Staff)]
    public async Task<ActionResult<LessonDto>> Create(LessonRequest request, CancellationToken ct)
    {
        var lesson = new Lesson();
        Apply(request, lesson);
        db.Lessons.Add(lesson);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (e.IsForeignKeyViolation())
        {
            return UnknownReference();
        }

        return CreatedAtAction(nameof(GetById), new { id = lesson.Id }, await LoadDtoAsync(lesson.Id, ct));
    }

    [HttpPut("{id:int}")]
    [Authorize(Policy = AppPolicies.Staff)]
    public async Task<ActionResult<LessonDto>> Update(int id, LessonRequest request, CancellationToken ct)
    {
        var lesson = await db.Lessons.FindAsync([id], ct);
        if (lesson is null)
        {
            return NotFound();
        }

        Apply(request, lesson);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (e.IsForeignKeyViolation())
        {
            return UnknownReference();
        }

        return (await LoadDtoAsync(id, ct))!;
    }

    [HttpDelete("{id:int}")]
    [Authorize(Policy = AppPolicies.Staff)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var lesson = await db.Lessons.FindAsync([id], ct);
        if (lesson is null)
        {
            return NotFound();
        }

        await materials.DeleteLessonAsync(lesson, ct);
        return NoContent();
    }

    [HttpGet("{id:int}/summary")]
    public async Task<ActionResult<LessonSummaryDto>> GetSummary(int id, CancellationToken ct)
    {
        var summary = await summaries.GetAsync(id, ct);
        return summary is null ? NotFound() : summary;
    }

    [HttpPut("{id:int}/summary")]
    [Authorize(Policy = AppPolicies.Staff)]
    public async Task<ActionResult<LessonSummaryDto>> SaveSummary(int id, LessonSummaryRequest request, CancellationToken ct)
    {
        var result = await summaries.SaveAsync(id, request.Markdown, request.Version, ct);
        return result.Status switch
        {
            LessonSummarySaveStatus.Saved => result.Summary!,
            LessonSummarySaveStatus.Conflict => Conflict(result.Summary),
            _ => NotFound()
        };
    }

    private Task<LessonDto?> LoadDtoAsync(int id, CancellationToken ct) =>
        db.Lessons.AsNoTracking().Where(l => l.Id == id).Select(LessonDto.Projection).FirstOrDefaultAsync(ct);

    private ActionResult UnknownReference() =>
        Problem(
            detail: "The referenced tutor, student or subject does not exist.",
            statusCode: StatusCodes.Status400BadRequest,
            title: "Unknown reference");

    private static void Apply(LessonRequest request, Lesson lesson)
    {
        lesson.TutorId = request.TutorId!.Value;
        lesson.StudentId = request.StudentId!.Value;
        lesson.SubjectId = request.SubjectId!.Value;
        lesson.Topic = request.Topic;
        lesson.StartsAt = request.StartsAt!.Value.ToUniversalTime();
        lesson.DurationMinutes = request.DurationMinutes;
        lesson.MeetingUrl = request.MeetingUrl;
        lesson.Status = request.Status;
        lesson.Notes = request.Notes;
    }
}
