using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.EntityFrameworkCore;
using TutorHub.Contracts;
using TutorHub.Data;
using TutorHub.Models;

namespace TutorHub.Controllers.Api;

[Route("api/students")]
public class StudentsApiController(AppDbContext db) : ApiControllerBase
{
    [HttpGet]
    [OutputCache(PolicyName = CacheInvalidationInterceptor.PolicyName)]
    public Task<PagedResult<StudentDto>> GetAll([FromQuery] PageQuery page, CancellationToken ct) =>
        db.Students.AsNoTracking()
            .OrderBy(s => s.FullName).ThenBy(s => s.Id)
            .Select(StudentDto.Projection)
            .ToPagedResultAsync(page, Request, ct);

    [HttpGet("{id:int}")]
    [OutputCache(PolicyName = CacheInvalidationInterceptor.PolicyName)]
    public async Task<ActionResult<StudentDto>> GetById(int id, CancellationToken ct)
    {
        var dto = await db.Students.AsNoTracking().Where(s => s.Id == id).Select(StudentDto.Projection).FirstOrDefaultAsync(ct);
        return dto is null ? NotFound() : dto;
    }

    [HttpPost]
    public async Task<ActionResult<StudentDto>> Create(StudentRequest request, CancellationToken ct)
    {
        var student = new Student();
        Apply(request, student);
        db.Students.Add(student);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (e.IsUniqueViolation())
        {
            return ConflictProblem("A student with this email already exists.");
        }

        return CreatedAtAction(nameof(GetById), new { id = student.Id }, ToDto(student));
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<StudentDto>> Update(int id, StudentRequest request, CancellationToken ct)
    {
        var student = await db.Students.FindAsync([id], ct);
        if (student is null)
        {
            return NotFound();
        }

        Apply(request, student);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (e.IsUniqueViolation())
        {
            return ConflictProblem("A student with this email already exists.");
        }

        return ToDto(student);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var student = await db.Students.FindAsync([id], ct);
        if (student is null)
        {
            return NotFound();
        }

        db.Students.Remove(student);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (e.IsForeignKeyViolation())
        {
            return ConflictProblem("The student cannot be deleted while lessons reference them.");
        }

        return NoContent();
    }

    private static void Apply(StudentRequest request, Student student)
    {
        student.FullName = request.FullName;
        student.Email = request.Email;
        student.Phone = request.Phone;
        student.LearningGoal = request.LearningGoal;
    }

    private static StudentDto ToDto(Student s) => new(s.Id, s.FullName, s.Email, s.Phone, s.LearningGoal);
}
