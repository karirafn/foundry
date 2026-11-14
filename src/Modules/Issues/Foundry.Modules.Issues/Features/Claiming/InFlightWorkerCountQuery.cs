using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts.Queries;

using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Issues.Features.Claiming;

/// <summary>
/// Returns the number of in-flight workers per repository, which the Repository Worker Limit bounds.
/// In-flight states are <see cref="InProgressIssue"/>, <see cref="RevisionInProgressIssue"/> and
/// <see cref="TriageInProgressIssue"/>, minus the issues whose run's <c>claude</c> container has ended with commits not
/// yet published: such a run holds no Worker Slot (DOMAIN.md), so it does not count here either.
/// Issues in review do not count because they have no running container.
/// </summary>
/// <remarks>
/// This query is independent of any dashboard issue set — it is not derived from a filtered
/// view but counts directly from the three in-flight discriminator states, grouped by repository.
/// <see cref="IssueClaimer.ClaimAsync"/> transitions in the same transaction as the claim,
/// so the count is accurate on the next tick with no uncounted window. Only issues with an unpublished run are
/// subtracted, never issues without a slot-holding run: a just-claimed issue has no run yet and must still count.
/// </remarks>
internal sealed class InFlightWorkerCountQuery(DbContext db, IWorkerRunQueries workerRuns)
{
    public async Task<IReadOnlyDictionary<MonitoredRepositoryId, int>> GetCountsAsync(
        CancellationToken cancellationToken)
    {
        // Select IDs as raw Guids — grouping on a value-converted composite
        // property is not translatable by SQLite EF Core; project to primitives first.
        List<InFlightIssue> freshInProgress = await db.Set<InProgressIssue>()
            .AsNoTracking()
            .Select(i => new InFlightIssue(i.Id.Value, i.MonitoredRepositoryId.Value))
            .ToListAsync(cancellationToken);

        List<InFlightIssue> revisionInProgress = await db.Set<RevisionInProgressIssue>()
            .AsNoTracking()
            .Select(i => new InFlightIssue(i.Id.Value, i.MonitoredRepositoryId.Value))
            .ToListAsync(cancellationToken);

        // A triage run is a worker like any other: it counts against the Repository Worker Limit (ADR 0097).
        List<InFlightIssue> triageInProgress = await db.Set<TriageInProgressIssue>()
            .AsNoTracking()
            .Select(i => new InFlightIssue(i.Id.Value, i.MonitoredRepositoryId.Value))
            .ToListAsync(cancellationToken);

        List<InFlightIssue> inFlight = [.. freshInProgress, .. revisionInProgress, .. triageInProgress];
        if (inFlight.Count == 0)
        {
            return new Dictionary<MonitoredRepositoryId, int>();
        }

        IReadOnlySet<Guid> publishing = await workerRuns.GetIssueIdsWithUnpublishedRunAsync(
            [.. inFlight.Select(i => i.IssueId)],
            cancellationToken);

        return inFlight
            .Where(i => !publishing.Contains(i.IssueId))
            .CountBy(i => i.RepositoryId)
            .ToDictionary(
                kvp => MonitoredRepositoryId.From(kvp.Key),
                kvp => kvp.Value);
    }

    private sealed record InFlightIssue(Guid IssueId, Guid RepositoryId);
}
