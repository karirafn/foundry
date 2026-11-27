using Foundry.Modules.Issues.Contracts.Events;
using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Features.Webhooks.Nudges;

/// <summary>
/// Nudges the repository's dependency pass for an issue that entered the triage queue, so blockers linked
/// before the trigger label are applied straight away rather than on the next scheduled poll. Nudges never
/// change state, so redelivery only adds a harmless, coalesced nudge; the scheduled poll is the backstop when
/// the nudge is lost (e.g. a restart).
/// </summary>
[IntegrationEventHandlerIdentity("Monitoring.IssueEnteredTriageQueue")]
internal sealed class IssueEnteredTriageQueueHandler(WebhookNudgeQueue nudgeQueue)
    : IIntegrationEventHandler<IssueEnteredTriageQueue>
{
    public Task HandleAsync(IssueEnteredTriageQueue @event, CancellationToken cancellationToken)
    {
        nudgeQueue.TryEnqueue(new Nudge(new NudgeKey(@event.RepositoryId, NudgePass.Dependencies)));
        return Task.CompletedTask;
    }
}
