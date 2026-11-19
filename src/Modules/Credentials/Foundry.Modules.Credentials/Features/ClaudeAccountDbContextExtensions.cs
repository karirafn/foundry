using Foundry.Modules.Credentials.Domain.Entities;
using Foundry.Modules.Credentials.Domain.ValueObjects;

using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Credentials.Features;

internal static class ClaudeAccountDbContextExtensions
{
    internal static Task<ClaudeAccount?> FindClaudeAccountAsync(this DbContext dbContext, CancellationToken cancellationToken) =>
        ClaudeAccountRow(dbContext).FirstOrDefaultAsync(cancellationToken);

    internal static Task<ClaudeAccount?> FindClaudeAccountNoTrackingAsync(this DbContext dbContext, CancellationToken cancellationToken) =>
        ClaudeAccountRow(dbContext).AsNoTracking().FirstOrDefaultAsync(cancellationToken);

    private static IQueryable<ClaudeAccount> ClaudeAccountRow(DbContext dbContext) =>
        dbContext.Set<ClaudeAccount>().Where(a => a.Id == ClaudeAccountId.Default);
}
