using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Shared;

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

internal sealed class IssueStateChangedAdapter<T>(IssueStateChangedHandler handler)
    : IDomainEventHandler<T>
    where T : IDomainEvent, IIssueStateChanged
{
    public Task HandleAsync(T @event, CancellationToken cancellationToken)
        => handler.HandleAsync(@event, cancellationToken);
}
