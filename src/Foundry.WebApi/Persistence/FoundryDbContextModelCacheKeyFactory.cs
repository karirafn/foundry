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
            return new ModelCacheKey(context.GetType(), foundryContext.DataProtectionProvider, foundryContext.ContextLoggerFactory, designTime);
        }

        return new ModelCacheKey(context.GetType(), null, null, designTime);
    }

    public object Create(DbContext context)
        => Create(context, designTime: false);

    private sealed record ModelCacheKey(
        Type ContextType,
        IDataProtectionProvider? Provider,
        ILoggerFactory? LoggerFactory,
        bool DesignTime);
}
