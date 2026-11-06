using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.EntityFrameworkCore;
using TutorHub.Contracts;
using TutorHub.Data;
using TutorHub.Models;

namespace TutorHub.Controllers.Api;

[Authorize(Policy = AppPolicies.Staff)]
[Route("api/tutors")]
public class TutorsApiController(AppDbContext db) : ApiControllerBase
{
    [HttpGet]
    [OutputCache(PolicyName = CacheInvalidationInterceptor.PolicyName)]
    public Task<PagedResult<TutorDto>> GetAll([FromQuery] PageQuery page, CancellationToken ct) =>
        db.Tutors.AsNoTracking()
            .OrderBy(t => t.FullName).ThenBy(t => t.Id)
            .Select(TutorDto.Projection)
            .ToPagedResultAsync(page, Request, ct);

    [HttpGet("{id:int}")]
    [OutputCache(PolicyName = CacheInvalidationInterceptor.PolicyName)]
    public async Task<ActionResult<TutorDto>> GetById(int id, CancellationToken ct)
    {
        var dto = await db.Tutors.AsNoTracking().Where(t => t.Id == id).Select(TutorDto.Projection).FirstOrDefaultAsync(ct);
        return dto is null ? NotFound() : dto;
    }

    [HttpPost]
    public async Task<ActionResult<TutorDto>> Create(TutorRequest request, CancellationToken ct)
    {
        var tutor = new Tutor();
        Apply(request, tutor);
        db.Tutors.Add(tutor);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (e.IsUniqueViolation())
        {
            return ConflictProblem("A tutor with this email already exists.");
        }

        return CreatedAtAction(nameof(GetById), new { id = tutor.Id }, ToDto(tutor));
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<TutorDto>> Update(int id, TutorRequest request, CancellationToken ct)
    {
        var tutor = await db.Tutors.FindAsync([id], ct);
        if (tutor is null)
        {
            return NotFound();
        }

        Apply(request, tutor);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (e.IsUniqueViolation())
        {
            return ConflictProblem("A tutor with this email already exists.");
        }

        return ToDto(tutor);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var tutor = await db.Tutors.FindAsync([id], ct);
        if (tutor is null)
        {
            return NotFound();
        }

        db.Tutors.Remove(tutor);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (e.IsForeignKeyViolation())
        {
            return ConflictProblem("The tutor cannot be deleted while lessons reference them.");
        }

        return NoContent();
    }

    private static void Apply(TutorRequest request, Tutor tutor)
    {
        tutor.FullName = request.FullName;
        tutor.Email = request.Email;
        tutor.Phone = request.Phone;
        tutor.Bio = request.Bio;
        tutor.HourlyRate = request.HourlyRate!.Value;
    }

    private static TutorDto ToDto(Tutor t) => new(t.Id, t.FullName, t.Email, t.Phone, t.Bio, t.HourlyRate);
}
