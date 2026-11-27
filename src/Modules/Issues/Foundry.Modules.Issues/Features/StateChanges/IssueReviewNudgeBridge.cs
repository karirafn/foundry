using Foundry.Modules.Issues.Contracts.Events;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Shared;

namespace Foundry.Modules.Issues.Features.StateChanges;

/// <summary>
/// Bridges the <see cref="IssueInReview"/> domain event to the <see cref="IssueEnteredReview"/> integration event, so
/// Monitoring can nudge its review pass to mark the draft pull request ready (ADR 0120).
/// </summary>
internal sealed class IssueReviewNudgeBridge(IIntegrationEventDispatcher integrationEventDispatcher)
    : IDomainEventHandler<IssueInReview>
{
    public Task HandleAsync(IssueInReview @event, CancellationToken cancellationToken)
        => integrationEventDispatcher.DispatchAsync(
            [new IssueEnteredReview(@event.MonitoredRepositoryId, @event.IssueNumber)],
            cancellationToken);
}
