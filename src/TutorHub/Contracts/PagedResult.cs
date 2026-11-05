using Microsoft.EntityFrameworkCore;

namespace TutorHub.Contracts;

public record PagedResult<T>(IReadOnlyList<T> Items, int Skip, int Limit, int Total, string? NextLink);

public static class PagedResultExtensions
{
    public static async Task<PagedResult<T>> ToPagedResultAsync<T>(
        this IQueryable<T> query,
        PageQuery page,
        HttpRequest request,
        CancellationToken ct)
    {
        var total = await query.CountAsync(ct);
        var items = await query.Skip(page.Skip).Take(page.Limit).ToListAsync(ct);

        var nextSkip = (long)page.Skip + page.Limit;
        var nextLink = nextSkip < total
            ? $"{request.PathBase}{request.Path}?skip={nextSkip}&limit={page.Limit}"
            : null;

        return new PagedResult<T>(items, page.Skip, page.Limit, total, nextLink);
    }
}
