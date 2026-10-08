using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging;

namespace Foundry.WebApi.Persistence;

internal sealed class FoundryDbContextModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime)
    {
        if (context is FoundryDbContext foundryContext)
        {
            return new ModelCacheKey(
                context.GetType(),
                foundryContext.DataProtectionProvider,
                foundryContext.ContextLoggerFactory,
                designTime);
        }

        // Contexts that are not FoundryDbContext (e.g. design-time scaffolding) carry no captured
        // provider or logger factory, so keying on the context type alone is correct and sufficient.
        return new ModelCacheKey(context.GetType(), null, null, designTime);
    }

    public object Create(DbContext context)
        => Create(context, designTime: false);

    // EF record equality falls back to reference equality for IDataProtectionProvider and
    // ILoggerFactory because those are interfaces. Two distinct provider instances therefore
    // produce distinct cache keys, so different key rings never share a cached model. The
    // DI-registered provider is assumed to be a singleton, meaning all DI-constructed contexts
    // resolve to a single cached model; the shared static default (SharedDefaultProvider on
    // FoundryDbContext) provides the same guarantee for contexts constructed without a provider.
    private sealed record ModelCacheKey(
        Type ContextType,
        IDataProtectionProvider? Provider,
        ILoggerFactory? LoggerFactory,
        bool DesignTime);
}
