using System.Linq.Expressions;

using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;

using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Workers.Features.Dispatch;

internal static class WorkerRunsDbContextExtensions
{
    // A run holds its dispatch slot until publishing finishes (ADR 0069, ADR 0100); one whose publish retries are
    // exhausted waits for an operator in Needs attention and frees its slot.
    private static readonly Expression<Func<WorkerRun, bool>> OccupiesSlot = r =>
        r is StartingRun
        || r is ActiveRun
        || r is PublishingRun
        || (r is PublishFailedRun && ((PublishFailedRun)r).NextPublishAttemptAt != null);

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
    /// Returns the count of items currently occupying a dispatch slot —
    /// pending reservations plus runs in the <c>starting</c>, <c>active</c> or <c>publishing</c> state, or retrying a
    /// publish.
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
            .CountAsync(OccupiesSlot, cancellationToken);

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
            .Where(OccupiesSlot)
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);

        HashSet<WorkerRunId> result = [..reservationIds, ..runIds];
        return result;
    }
}
