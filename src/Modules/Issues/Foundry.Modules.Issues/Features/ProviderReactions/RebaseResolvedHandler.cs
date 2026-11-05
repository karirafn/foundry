using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Issues.Features.ProviderReactions;

/// <summary>
/// Moves a rebase-pending issue on to the checks gate once Monitoring's rebase step resolved a
/// <see cref="RebaseOutcome"/> (ADR 0087). Any other state — including an issue that already left
/// rebase-pending on an earlier delivery — is ignored, so redelivery is a no-op beyond inbox dedup.
/// </summary>
[IntegrationEventHandlerIdentity("Issues.RebaseResolved")]
internal sealed class RebaseResolvedHandler(
    DbContext db,
    IDomainEventDispatcher domainEventDispatcher,
    ILogger<RebaseResolvedHandler> logger) : IIntegrationEventHandler<RebaseResolved>
{
    public async Task HandleAsync(RebaseResolved @event, CancellationToken cancellationToken)
    {
        Issue? issue = await db.Set<Issue>()
            .Where(i => i.MonitoredRepositoryId == @event.RepositoryId)
            .FirstOrDefaultAsync(i => i.IssueNumber == @event.IssueNumber, cancellationToken);

        if (issue is not RebasePendingIssue rebasePendingIssue)
        {
            logger.LogWarning(
                "RebaseResolved received for repository {RepositoryId} issue {IssueNumber} but it is not a RebasePendingIssue (state: {State}); ignoring.",
                @event.RepositoryId,
                @event.IssueNumber,
                issue?.GetType().Name ?? "not found");
            return;
        }

        // An unknown discriminator (a newer producer across a deploy boundary) fails open as Skipped.
        RebaseOutcome outcome = RebaseOutcome.FromDiscriminator(@event.Outcome, @event.Reason)
            ?? new RebaseOutcome.Skipped($"unknown-outcome:{@event.Outcome}");

        ChecksPendingIssue checksPending = rebasePendingIssue.MarkChecksPending(outcome);
        await db.TransitionAsync(rebasePendingIssue, checksPending, domainEventDispatcher, cancellationToken);
    }
}
