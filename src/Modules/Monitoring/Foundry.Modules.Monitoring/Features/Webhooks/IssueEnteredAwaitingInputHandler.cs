using Foundry.Modules.Issues.Contracts.Events;
using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Features.Webhooks;

/// <summary>
/// Nudges the awaiting-input step for an issue that entered awaiting input (ADR 0096), so its questions are posted
/// straight away rather than on the next scheduled poll. Nudges never change state, so redelivery only adds a
/// harmless, coalesced nudge; the scheduled poll is the backstop.
/// </summary>
[IntegrationEventHandlerIdentity("Monitoring.IssueEnteredAwaitingInput")]
internal sealed class IssueEnteredAwaitingInputHandler(WebhookNudgeQueue nudgeQueue)
    : IIntegrationEventHandler<IssueEnteredAwaitingInput>
{
    public Task HandleAsync(IssueEnteredAwaitingInput @event, CancellationToken cancellationToken)
    {
        nudgeQueue.TryEnqueue(new Nudge(new NudgeKey(@event.RepositoryId, NudgePass.AwaitingInput, @event.IssueNumber)));
        return Task.CompletedTask;
    }
}
