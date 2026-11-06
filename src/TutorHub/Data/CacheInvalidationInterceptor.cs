using System.Security.Claims;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace TutorHub.Data;

public class CacheInvalidationInterceptor(IOutputCacheStore cache) : SaveChangesInterceptor
{
    public const string PolicyName = "api";
    public const string Tag = "api";

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        if (result > 0)
        {
            cache.EvictByTagAsync(Tag, CancellationToken.None).AsTask().GetAwaiter().GetResult();
        }

        return result;
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        if (result > 0)
        {
            await cache.EvictByTagAsync(Tag, CancellationToken.None);
        }

        return result;
    }
}

public class UserOutputCachePolicy : IOutputCachePolicy
{
    public ValueTask CacheRequestAsync(OutputCacheContext context, CancellationToken cancellation)
    {
        var request = context.HttpContext.Request;
        var userId = context.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var cacheable = userId is not null && (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method));

        context.EnableOutputCaching = true;
        context.AllowCacheLookup = cacheable;
        context.AllowCacheStorage = cacheable;
        context.AllowLocking = true;
        context.ResponseExpirationTimeSpan = TimeSpan.FromSeconds(60);
        context.CacheVaryByRules.QueryKeys = "*";
        context.CacheVaryByRules.VaryByValues["user"] = userId ?? "";
        context.Tags.Add(CacheInvalidationInterceptor.Tag);

        return ValueTask.CompletedTask;
    }

    public ValueTask ServeFromCacheAsync(OutputCacheContext context, CancellationToken cancellation) =>
        ValueTask.CompletedTask;

    public ValueTask ServeResponseAsync(OutputCacheContext context, CancellationToken cancellation)
    {
        var response = context.HttpContext.Response;
        if (response.StatusCode != StatusCodes.Status200OK || response.Headers.SetCookie.Count > 0)
        {
            context.AllowCacheStorage = false;
        }

        return ValueTask.CompletedTask;
    }
}
