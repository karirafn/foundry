using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;

using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Issues.Features.StateChanges;

/// <summary>
/// Broadcasts <c>IssueUpdated</c> for each issue whose catch-up queue entry changed without a state change (ADR 0111),
/// so an open issue pane reloads its detail and shows the new position, awaited issue or round.
/// </summary>
[IntegrationEventHandlerIdentity("Issues.MergeQueueChangedBroadcast")]
internal sealed class MergeQueueChangedHandler(DbContext db, IssueStateChangedHandler stateChangedHandler)
    : IIntegrationEventHandler<MergeQueueChanged>
{
    public async Task HandleAsync(MergeQueueChanged @event, CancellationToken cancellationToken)
    {
        List<int> issueNumbers = [.. @event.IssueNumbers];
        List<IssueId> issueIds = await db.Set<Issue>()
            .AsNoTracking()
            .Where(i => i.MonitoredRepositoryId == @event.RepositoryId && issueNumbers.Contains(i.IssueNumber))
            .OrderBy(i => i.IssueNumber)
            .Select(i => i.Id)
            .ToListAsync(cancellationToken);

        foreach (IssueId issueId in issueIds)
        {
            await stateChangedHandler.BroadcastSummaryAsync(issueId, cancellationToken);
        }
    }
}
