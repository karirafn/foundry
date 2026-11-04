using Foundry.Modules.Issues.Contracts.Events;
using Foundry.Modules.Monitoring.Features.Polling;
using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Features.Webhooks;

/// <summary>
/// Schedules the checks-gate nudges for an issue that entered checks-pending (ADR 0085, ADR 0077): one
/// immediately, and not-before nudges at the grace-window and upper-bound deadlines — time-based exits no
/// provider event can trigger — so a CI-less repository leaves checks-pending on time under the backstop.
/// Nudges never change state, so redelivery only adds harmless, coalesced nudges.
/// </summary>
[IntegrationEventHandlerIdentity("Monitoring.IssueEnteredChecksPending")]
internal sealed class IssueEnteredChecksPendingHandler(WebhookNudgeQueue nudgeQueue)
    : IIntegrationEventHandler<IssueEnteredChecksPending>
{
    public Task HandleAsync(IssueEnteredChecksPending @event, CancellationToken cancellationToken)
    {
        NudgeKey key = new(@event.RepositoryId, NudgePass.Checks, @event.IssueNumber);

        nudgeQueue.TryEnqueue(new Nudge(key));
        nudgeQueue.TryEnqueue(new Nudge(key, @event.EnteredChecksPendingAt + RepositoryPoller.ChecksNoneGraceWindow));
        nudgeQueue.TryEnqueue(new Nudge(key, @event.EnteredChecksPendingAt + RepositoryPoller.ChecksPendingUpperBound));

        return Task.CompletedTask;
    }
}
