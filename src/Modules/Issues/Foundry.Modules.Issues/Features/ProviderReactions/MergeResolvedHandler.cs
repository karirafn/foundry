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
/// Applies Monitoring's merge step outcome to a merge-pending issue (ADR 0105): <c>merged</c> completes it, and
/// <c>disabled</c> or <c>skipped</c> sends it to review with the outcome recorded, and <c>feedback</c> revises it with
/// the person's comments (issue #917). Any other state — including an
/// issue that already left merge-pending on an earlier delivery — is ignored, so redelivery is a no-op beyond
/// inbox dedup.
/// </summary>
[IntegrationEventHandlerIdentity("Issues.MergeResolved")]
internal sealed class MergeResolvedHandler(
    DbContext db,
    IDomainEventDispatcher domainEventDispatcher,
    TimeProvider timeProvider,
    ILogger<MergeResolvedHandler> logger) : IIntegrationEventHandler<MergeResolved>
{
    public async Task HandleAsync(MergeResolved @event, CancellationToken cancellationToken)
    {
        Issue? issue = await db.Set<Issue>()
            .Where(i => i.MonitoredRepositoryId == @event.RepositoryId)
            .FirstOrDefaultAsync(i => i.IssueNumber == @event.IssueNumber, cancellationToken);

        if (issue is not MergePendingIssue mergePendingIssue)
        {
            logger.LogWarning(
                "MergeResolved received for repository {RepositoryId} issue {IssueNumber} but it is not a MergePendingIssue (state: {State}); ignoring.",
                @event.RepositoryId,
                @event.IssueNumber,
                issue?.GetType().Name ?? "not found");
            return;
        }

        DateTimeOffset now = timeProvider.GetUtcNow();

        // A person's review feedback stopped the merge (issue #917): it drives a revision, as from checks-pending.
        if (@event.Outcome == MergeResolved.FeedbackOutcome)
        {
            RevisionQueuedIssue revisionQueued = mergePendingIssue.Revise(
                @event.Comments ?? [],
                now,
                @event.OmittedCommentCount,
                @event.NewestCommentAt);
            await db.TransitionAsync(mergePendingIssue, revisionQueued, domainEventDispatcher, cancellationToken);
            return;
        }

        // An unknown discriminator (a newer producer across a deploy boundary) fails safe to review as Skipped.
        MergeOutcome outcome = MergeOutcome.FromDiscriminator(@event.Outcome, @event.Reason)
            ?? new MergeOutcome.Skipped($"unknown-outcome:{@event.Outcome}");

        Issue next = outcome is MergeOutcome.Merged
            ? mergePendingIssue.Complete(now)
            : mergePendingIssue.ToReview(outcome, now);
        await db.TransitionAsync(mergePendingIssue, next, domainEventDispatcher, cancellationToken);
    }
}
