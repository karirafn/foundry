using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Shared;

using Microsoft.Extensions.Caching.Memory;

namespace Foundry.Modules.Monitoring.Infrastructure;

/// <summary>
/// Caches default-branch lookups by (host, slug) so repeated calls within the TTL
/// skip the HTTP round-trip. Keyed by (apiBaseUrl.Host, slug.FullPath) to avoid
/// collisions between different hosts or repositories.
/// </summary>
internal sealed class DefaultBranchCache(IMemoryCache memoryCache)
{
    private static readonly TimeSpan SlidingExpiration = TimeSpan.FromHours(1);

    public async Task<Result<string>> GetOrFetchAsync(
        Uri apiBaseUrl,
        RepositorySlug slug,
        Func<Task<Result<string>>> fetch)
    {
        string cacheKey = $"{apiBaseUrl.Host}:{slug.FullPath}";

        if (memoryCache.TryGetValue(cacheKey, out string? cached) && cached is not null)
        {
            return Result<string>.Ok(cached);
        }

        Result<string> result = await fetch();

        if (result is Result<string>.Success success)
        {
            MemoryCacheEntryOptions options = new()
            {
                SlidingExpiration = SlidingExpiration,
            };
            memoryCache.Set(cacheKey, success.Value, options);
        }

        return result;
    }
}
