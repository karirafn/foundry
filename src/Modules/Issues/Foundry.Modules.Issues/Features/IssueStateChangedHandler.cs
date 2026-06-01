using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Events;

namespace Foundry.Modules.Issues.Features;

internal sealed class IssueStateChangedHandler(IIssueQueries issueQueries, IIssueBroadcaster broadcaster)
{
    public async Task HandleAsync(IIssueStateChanged @event, CancellationToken cancellationToken)
    {
        IssueSummary? summary = await issueQueries.GetIssueSummaryAsync(@event.IssueId, cancellationToken);

        if (summary is null)
        {
            return;
        }

        await broadcaster.BroadcastAsync(summary, cancellationToken);
    }
}
