using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Monitoring.Contracts;

using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Issues.Features.Claiming;

/// <summary>
/// Returns the number of actively running workers per repository.
/// In-flight states are <see cref="InProgressIssue"/> and <see cref="RevisionInProgressIssue"/>;
/// issues in review do not count because they have no running container.
/// </summary>
/// <remarks>
/// This query is independent of any dashboard issue set — it is not derived from a filtered
/// view but counts directly from the two in-flight discriminator states, grouped by repository.
/// <see cref="IssueClaimer.ClaimAsync"/> transitions in the same transaction as the claim,
/// so the count is accurate on the next tick with no uncounted window.
/// </remarks>
internal sealed class InFlightWorkerCountQuery(DbContext db)
{
    public async Task<IReadOnlyDictionary<MonitoredRepositoryId, int>> GetCountsAsync(
        CancellationToken cancellationToken)
    {
        // Select repository IDs as raw Guids — grouping on a value-converted composite
        // property is not translatable by SQLite EF Core; project to primitives first.
        List<Guid> freshInProgress = await db.Set<InProgressIssue>()
            .Select(i => i.MonitoredRepositoryId.Value)
            .ToListAsync(cancellationToken);

        List<Guid> revisionInProgress = await db.Set<RevisionInProgressIssue>()
            .Select(i => i.MonitoredRepositoryId.Value)
            .ToListAsync(cancellationToken);

        Dictionary<Guid, int> countByGuid = freshInProgress
            .Concat(revisionInProgress)
            .CountBy(id => id)
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        return countByGuid.ToDictionary(
            kvp => MonitoredRepositoryId.From(kvp.Key),
            kvp => kvp.Value);
    }
}
