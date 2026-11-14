using Foundry.Modules.Issues.Contracts.Events;
using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Features.Webhooks;

/// <summary>
/// Nudges the merge step for an issue that entered merge-pending (ADR 0105), so the merge runs straight away
/// rather than on the next scheduled poll. Nudges never change state, so redelivery only adds a harmless,
/// coalesced nudge; the scheduled poll is the backstop when the nudge is lost (e.g. a restart).
/// </summary>
[IntegrationEventHandlerIdentity("Monitoring.IssueEnteredMergePending")]
internal sealed class IssueEnteredMergePendingHandler(WebhookNudgeQueue nudgeQueue)
    : IIntegrationEventHandler<IssueEnteredMergePending>
{
    public Task HandleAsync(IssueEnteredMergePending @event, CancellationToken cancellationToken)
    {
        nudgeQueue.TryEnqueue(new Nudge(new NudgeKey(@event.RepositoryId, NudgePass.Merge, @event.IssueNumber)));
        return Task.CompletedTask;
    }
}
