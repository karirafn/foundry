using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;

using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Workers.Features.Dispatch;

internal static class WorkerRunsDbContextExtensions
{
    /// <summary>
    /// Returns the count of items currently occupying a dispatch slot —
    /// pending reservations plus runs in the <c>starting</c> or <c>active</c> state.
    /// </summary>
    internal static async Task<int> GetSlotOccupancyCountAsync(
        this DbContext dbContext,
        CancellationToken cancellationToken = default)
    {
        // Two separate CountAsync calls to preserve SQL translation (A4).
        int reservationCount = await dbContext.Set<DispatchReservation>()
            .AsNoTracking()
            .CountAsync(cancellationToken);

        int runCount = await dbContext.Set<WorkerRun>()
            .AsNoTracking()
            .CountAsync(r => r is StartingRun || r is ActiveRun, cancellationToken);

        return reservationCount + runCount;
    }

    /// <summary>
    /// Returns the set of ids currently occupying a dispatch slot —
    /// pending reservation ids plus run ids in the <c>starting</c> or <c>active</c> state.
    /// </summary>
    internal static async Task<IReadOnlySet<WorkerRunId>> GetSlotOccupancyRunIdsAsync(
        this DbContext dbContext,
        CancellationToken cancellationToken = default)
    {
        List<WorkerRunId> reservationIds = await dbContext.Set<DispatchReservation>()
            .AsNoTracking()
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);

        List<WorkerRunId> runIds = await dbContext.Set<WorkerRun>()
            .AsNoTracking()
            .Where(r => r is StartingRun || r is ActiveRun)
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);

        HashSet<WorkerRunId> result = [..reservationIds, ..runIds];
        return result;
    }
}
