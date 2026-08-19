using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;

using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Workers.Features.Dispatch;

internal static class WorkerRunsDbContextExtensions
{
    /// <summary>
    /// Returns the count of runs currently occupying a dispatch slot —
    /// those in the <c>starting</c> or <c>active</c> state.
    /// </summary>
    internal static Task<int> GetSlotOccupancyCountAsync(
        this DbContext dbContext,
        CancellationToken cancellationToken = default)
    {
        return dbContext.Set<WorkerRun>()
            .AsNoTracking()
            .CountAsync(r => r is StartingRun || r is ActiveRun, cancellationToken);
    }

    /// <summary>
    /// Returns the set of run ids currently occupying a dispatch slot —
    /// those in the <c>starting</c> or <c>active</c> state.
    /// </summary>
    internal static async Task<IReadOnlySet<WorkerRunId>> GetSlotOccupancyRunIdsAsync(
        this DbContext dbContext,
        CancellationToken cancellationToken = default)
    {
        List<WorkerRunId> ids = await dbContext.Set<WorkerRun>()
            .AsNoTracking()
            .Where(r => r is StartingRun || r is ActiveRun)
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);

        return ids.ToHashSet();
    }
}
