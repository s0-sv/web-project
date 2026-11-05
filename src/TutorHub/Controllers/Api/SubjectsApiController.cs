using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TutorHub.Contracts;
using TutorHub.Data;
using TutorHub.Models;

namespace TutorHub.Controllers.Api;

[Route("api/subjects")]
public class SubjectsApiController(AppDbContext db) : ApiControllerBase
{
    [HttpGet]
    public Task<PagedResult<SubjectDto>> GetAll([FromQuery] PageQuery page, CancellationToken ct) =>
        db.Subjects.AsNoTracking()
            .OrderBy(s => s.Name).ThenBy(s => s.Id)
            .Select(SubjectDto.Projection)
            .ToPagedResultAsync(page, Request, ct);

    [HttpGet("{id:int}")]
    public async Task<ActionResult<SubjectDto>> GetById(int id, CancellationToken ct)
    {
        var dto = await db.Subjects.AsNoTracking().Where(s => s.Id == id).Select(SubjectDto.Projection).FirstOrDefaultAsync(ct);
        return dto is null ? NotFound() : dto;
    }

    [HttpPost]
    public async Task<ActionResult<SubjectDto>> Create(SubjectRequest request, CancellationToken ct)
    {
        var subject = new Subject { Name = request.Name, Description = request.Description };
        db.Subjects.Add(subject);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (e.IsUniqueViolation())
        {
            return ConflictProblem("A subject with this name already exists.");
        }

        return CreatedAtAction(nameof(GetById), new { id = subject.Id }, ToDto(subject));
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<SubjectDto>> Update(int id, SubjectRequest request, CancellationToken ct)
    {
        var subject = await db.Subjects.FindAsync([id], ct);
        if (subject is null)
        {
            return NotFound();
        }

        subject.Name = request.Name;
        subject.Description = request.Description;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (e.IsUniqueViolation())
        {
            return ConflictProblem("A subject with this name already exists.");
        }

        return ToDto(subject);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var subject = await db.Subjects.FindAsync([id], ct);
        if (subject is null)
        {
            return NotFound();
        }

        db.Subjects.Remove(subject);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (e.IsForeignKeyViolation())
        {
            return ConflictProblem("The subject cannot be deleted while lessons reference it.");
        }

        return NoContent();
    }

    private static SubjectDto ToDto(Subject s) => new(s.Id, s.Name, s.Description);
}
