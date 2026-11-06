using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TutorHub.Data;
using TutorHub.Models;
using TutorHub.Services;

namespace TutorHub.Controllers;

public record MapTutor(
    string Name,
    string Slug,
    string City,
    double Latitude,
    double Longitude,
    decimal HourlyRate,
    string? Headline,
    IReadOnlyList<string> Subjects);

public record MapSubjectStat(string Name, int Tutors);

public record MapResponse(IReadOnlyList<MapTutor> Tutors, IReadOnlyList<MapSubjectStat> Subjects);

[AllowAnonymous]
public class MapController(AppDbContext db) : AppController
{
    public const int MaxChartSubjects = 10;

    private IQueryable<Tutor> Located =>
        db.Tutors.IgnoreQueryFilters().AsNoTracking()
            .Where(t => t.IsPublished && t.Slug != null && t.City != null && t.Latitude != null && t.Longitude != null);

    [HttpGet("/map")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        IReadOnlyList<string> subjects = await Located
            .SelectMany(t => t.Lessons)
            .Where(l => l.Status != LessonStatus.Cancelled)
            .Select(l => l.Subject!.Name)
            .Distinct()
            .OrderBy(name => name)
            .ToListAsync(ct);

        return View(subjects);
    }

    [HttpGet("/map/tutors")]
    public async Task<MapResponse> Tutors(string? subject, decimal? maxRate, string? q, CancellationToken ct)
    {
        var query = Located;

        if (!string.IsNullOrWhiteSpace(subject))
        {
            query = query.Where(t => t.Lessons.Any(l => l.Status != LessonStatus.Cancelled && l.Subject!.Name == subject));
        }

        if (maxRate is { } rate)
        {
            query = query.Where(t => t.HourlyRate <= rate);
        }

        foreach (var pattern in LikePatterns.FromWords(q))
        {
            query = query.Where(t => EF.Functions.ILike(t.FullName, pattern, "\\") || EF.Functions.ILike(t.City!, pattern, "\\"));
        }

        var tutors = await query
            .OrderBy(t => t.FullName).ThenBy(t => t.Id)
            .Select(t => new
            {
                t.FullName,
                t.Slug,
                t.City,
                t.Latitude,
                t.Longitude,
                t.HourlyRate,
                t.Headline,
                Subjects = t.Lessons
                    .Where(l => l.Status != LessonStatus.Cancelled)
                    .Select(l => l.Subject!.Name)
                    .Distinct()
                    .OrderBy(name => name)
                    .ToList()
            })
            .ToListAsync(ct);

        return new MapResponse(tutors
            .Select(t => new MapTutor(t.FullName, t.Slug!, t.City!, t.Latitude!.Value, t.Longitude!.Value, t.HourlyRate, t.Headline, t.Subjects))
            .ToList(),
            tutors.SelectMany(t => t.Subjects)
                .GroupBy(name => name)
                .Select(g => new MapSubjectStat(g.Key, g.Count()))
                .OrderByDescending(s => s.Tutors).ThenBy(s => s.Name, StringComparer.Ordinal)
                .Take(MaxChartSubjects)
                .ToList());
    }
}
