using Foundry.Modules.Issues.Contracts.Events;
using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Features.Webhooks.Nudges;

/// <summary>
/// Nudges the review pass for an issue that entered review, so its draft pull request is marked ready straight away
/// rather than on the next scheduled poll (ADR 0120). Nudges never change state, so redelivery only adds a harmless,
/// coalesced nudge; the scheduled poll is the backstop when the nudge is lost (e.g. a restart).
/// </summary>
[IntegrationEventHandlerIdentity("Monitoring.IssueEnteredReview")]
internal sealed class IssueEnteredReviewHandler(WebhookNudgeQueue nudgeQueue)
    : IIntegrationEventHandler<IssueEnteredReview>
{
    public Task HandleAsync(IssueEnteredReview @event, CancellationToken cancellationToken)
    {
        nudgeQueue.TryEnqueue(new Nudge(new NudgeKey(@event.RepositoryId, NudgePass.Review, @event.IssueNumber)));
        return Task.CompletedTask;
    }
}
