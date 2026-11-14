using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;

using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Workers.Features.Dispatch;

internal static class WorkerRunsDbContextExtensions
{
    /// <summary>
    /// The run ids whose per-run volumes must survive the orphan sweep: every run occupying a slot, plus every run
    /// still holding unpublished work in its Hand-off volume.
    /// </summary>
    internal static async Task<IReadOnlySet<WorkerRunId>> GetVolumeRetainingRunIdsAsync(
        this DbContext dbContext,
        CancellationToken cancellationToken = default)
    {
        IReadOnlySet<WorkerRunId> slotRunIds = await dbContext.GetSlotOccupancyRunIdsAsync(cancellationToken);

        List<WorkerRunId> unpublishedRunIds = await dbContext.Set<WorkerRun>()
            .AsNoTracking()
            .Where(r => r is UnpublishedRun)
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);

        HashSet<WorkerRunId> result = [.. slotRunIds, .. unpublishedRunIds];
        return result;
    }

    /// <summary>
    /// Returns the count of items currently occupying a dispatch slot — pending reservations plus the runs that
    /// <see cref="WorkerRun.HoldsSlot"/> (<c>starting</c> or <c>active</c>). A run frees its slot when its <c>claude</c>
    /// container stops, so publishing or retrying a publish holds none.
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
            .CountAsync(WorkerRun.HoldsSlot, cancellationToken);

        return reservationCount + runCount;
    }

    /// <summary>
    /// Returns the set of ids currently occupying a dispatch slot —
    /// pending reservation ids plus the ids of the runs <see cref="GetSlotOccupancyCountAsync"/> counts.
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
            .Where(WorkerRun.HoldsSlot)
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);

        HashSet<WorkerRunId> result = [..reservationIds, ..runIds];
        return result;
    }
}
