using Foundry.Modules.Issues.Contracts.Events;
using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Features.Webhooks;

/// <summary>
/// Nudges the rebase step for an issue that entered rebase-pending (ADR 0087), so the rebase runs straight
/// away rather than on the next scheduled poll. Nudges never change state, so redelivery only adds a harmless,
/// coalesced nudge; the scheduled poll is the backstop when the nudge is lost (e.g. a restart).
/// </summary>
[IntegrationEventHandlerIdentity("Monitoring.IssueEnteredRebasePending")]
internal sealed class IssueEnteredRebasePendingHandler(WebhookNudgeQueue nudgeQueue)
    : IIntegrationEventHandler<IssueEnteredRebasePending>
{
    public Task HandleAsync(IssueEnteredRebasePending @event, CancellationToken cancellationToken)
    {
        nudgeQueue.TryEnqueue(new Nudge(new NudgeKey(@event.RepositoryId, NudgePass.Rebase, @event.IssueNumber)));
        return Task.CompletedTask;
    }
}
