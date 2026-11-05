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
