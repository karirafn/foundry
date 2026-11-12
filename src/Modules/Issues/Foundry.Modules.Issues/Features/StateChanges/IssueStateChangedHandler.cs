using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Events;

namespace Foundry.Modules.Issues.Features.StateChanges;

internal sealed class IssueStateChangedHandler(IIssueQueries issueQueries, IIssueBroadcaster broadcaster)
{
    public Task HandleAsync(IIssueStateChanged @event, CancellationToken cancellationToken)
        => BroadcastSummaryAsync(@event.IssueId, cancellationToken);

    /// <summary>Broadcasts the issue's current summary, or nothing when the issue no longer exists.</summary>
    public async Task BroadcastSummaryAsync(IssueId issueId, CancellationToken cancellationToken)
    {
        IssueSummary? summary = await issueQueries.GetIssueSummaryAsync(issueId, cancellationToken);

        if (summary is null)
        {
            return;
        }

        await broadcaster.BroadcastAsync(summary, cancellationToken);
    }
}
