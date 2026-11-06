using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TutorHub.Data;
using TutorHub.Models;

namespace TutorHub.Controllers;

public class HomeController(AppDbContext db) : AppController
{
    public async Task<IActionResult> Index()
    {
        ViewBag.TutorCount = await db.Tutors.CountAsync();
        ViewBag.StudentCount = await db.Students.CountAsync();
        ViewBag.SubjectCount = await db.Subjects.CountAsync();
        ViewBag.LessonCount = await db.Lessons.CountAsync();

        var upcoming = await db.Lessons.AsNoTracking()
            .Include(l => l.Tutor)
            .Include(l => l.Student)
            .Include(l => l.Subject)
            .Where(l => l.Status == LessonStatus.Scheduled && l.StartsAt >= DateTimeOffset.UtcNow)
            .OrderBy(l => l.StartsAt)
            .Take(5)
            .ToListAsync();

        return View(upcoming);
    }

    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    [Route("/Home/Status/{code:int}")]
    public IActionResult Status(int code) => View("Status", code);

    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() =>
        View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
}
