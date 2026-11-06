using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TutorHub.Data;
using TutorHub.Models;

namespace TutorHub.Controllers;

public record LookupItem(int Id, string Text);

public record LookupResponse(IReadOnlyList<LookupItem> Results);

[Authorize(Policy = AppPolicies.Staff)]
public class LookupController(AppDbContext db) : AppController
{
    public const int MinQueryLength = 3;
    public const int MaxResults = 20;
    public const int DelayMilliseconds = 300;
    private const int MaxCandidates = 500;
    private const int MaxQueryLength = 100;
    private const int MaxWords = 5;

    public async Task<LookupResponse> Tutors(string? q, CancellationToken ct)
    {
        var patterns = Patterns(q);
        if (patterns.Count == 0)
        {
            return new LookupResponse([]);
        }

        var query = db.Tutors.AsNoTracking();
        foreach (var pattern in patterns)
        {
            query = query.Where(t => EF.Functions.ILike(t.FullName, pattern, "\\") || EF.Functions.ILike(t.Email, pattern, "\\"));
        }

        return new LookupResponse(await query
            .Take(MaxCandidates)
            .OrderBy(t => t.FullName).ThenBy(t => t.Id)
            .Take(MaxResults)
            .Select(t => new LookupItem(t.Id, t.FullName + " (" + t.Email + ")"))
            .ToListAsync(ct));
    }

    public async Task<LookupResponse> Students(string? q, CancellationToken ct)
    {
        var patterns = Patterns(q);
        if (patterns.Count == 0)
        {
            return new LookupResponse([]);
        }

        var query = db.Students.AsNoTracking();
        foreach (var pattern in patterns)
        {
            query = query.Where(s => EF.Functions.ILike(s.FullName, pattern, "\\") || EF.Functions.ILike(s.Email, pattern, "\\"));
        }

        return new LookupResponse(await query
            .Take(MaxCandidates)
            .OrderBy(s => s.FullName).ThenBy(s => s.Id)
            .Take(MaxResults)
            .Select(s => new LookupItem(s.Id, s.FullName + " (" + s.Email + ")"))
            .ToListAsync(ct));
    }

    public async Task<LookupResponse> Subjects(string? q, CancellationToken ct)
    {
        var patterns = Patterns(q);
        if (patterns.Count == 0)
        {
            return new LookupResponse([]);
        }

        var query = db.Subjects.AsNoTracking();
        foreach (var pattern in patterns)
        {
            query = query.Where(s => EF.Functions.ILike(s.Name, pattern, "\\"));
        }

        return new LookupResponse(await query
            .Take(MaxCandidates)
            .OrderBy(s => s.Name).ThenBy(s => s.Id)
            .Take(MaxResults)
            .Select(s => new LookupItem(s.Id, s.Name))
            .ToListAsync(ct));
    }

    private static List<string> Patterns(string? query)
    {
        var text = query?.Trim() ?? "";
        if (text.Length < MinQueryLength)
        {
            return [];
        }

        if (text.Length > MaxQueryLength)
        {
            text = text[..MaxQueryLength];
        }

        return text.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Take(MaxWords)
            .Select(word => $"%{word.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_")}%")
            .ToList();
    }
}
