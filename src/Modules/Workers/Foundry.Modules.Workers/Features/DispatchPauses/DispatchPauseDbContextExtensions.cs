using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.ValueObjects;

using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Workers.Features.DispatchPauses;

internal static class DispatchPauseDbContextExtensions
{
    internal static Task<DispatchPause?> FindDispatchPauseAsync(this DbContext dbContext, CancellationToken cancellationToken) =>
        DispatchPauseRow(dbContext).FirstOrDefaultAsync(cancellationToken);

    internal static Task<DispatchPause?> FindDispatchPauseNoTrackingAsync(this DbContext dbContext, CancellationToken cancellationToken) =>
        DispatchPauseRow(dbContext).AsNoTracking().FirstOrDefaultAsync(cancellationToken);

    private static IQueryable<DispatchPause> DispatchPauseRow(DbContext dbContext) =>
        dbContext.Set<DispatchPause>().Where(p => p.Id == DispatchPauseId.Default);
}
