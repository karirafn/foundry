using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Issues.Features.ProviderReactions;

[IntegrationEventHandlerIdentity("Issues.ProviderPullRequestClosed")]
internal sealed class ProviderPullRequestClosedHandler(
    DbContext db,
    IDomainEventDispatcher domainEventDispatcher,
    ILogger<ProviderPullRequestClosedHandler> logger) : IIntegrationEventHandler<ProviderPullRequestClosed>
{
    private const string FailureReason = "Pull request closed without merge";

    public async Task HandleAsync(ProviderPullRequestClosed @event, CancellationToken cancellationToken)
    {
        Issue? issue = await db.Set<Issue>()
            .Where(i => i.MonitoredRepositoryId == @event.RepositoryId)
            .FirstOrDefaultAsync(i => i.IssueNumber == @event.IssueNumber, cancellationToken);

        DateTimeOffset now = DateTimeOffset.UtcNow;

        switch (issue)
        {
            case ReviewIssue reviewIssue:
            {
                ContinuableFailedIssue failed = reviewIssue.Fail(FailureReason, FailureCategory.PrClosed, now);
                await db.TransitionAsync(reviewIssue, failed, domainEventDispatcher, cancellationToken);
                break;
            }

            case ChecksPendingIssue checksPendingIssue:
            {
                ContinuableFailedIssue failed = checksPendingIssue.Fail(FailureReason, FailureCategory.PrClosed, now);
                await db.TransitionAsync(checksPendingIssue, failed, domainEventDispatcher, cancellationToken);
                break;
            }

            case RebasePendingIssue rebasePendingIssue:
            {
                ContinuableFailedIssue failed = rebasePendingIssue.Fail(FailureReason, FailureCategory.PrClosed, now);
                await db.TransitionAsync(rebasePendingIssue, failed, domainEventDispatcher, cancellationToken);
                break;
            }

            case MergePendingIssue mergePendingIssue:
            {
                ContinuableFailedIssue failed = mergePendingIssue.Fail(FailureReason, FailureCategory.PrClosed, now);
                await db.TransitionAsync(mergePendingIssue, failed, domainEventDispatcher, cancellationToken);
                break;
            }

            case ReviewQueuedIssue reviewQueuedIssue:
            {
                ContinuableFailedIssue failed = reviewQueuedIssue.Fail(FailureReason, FailureCategory.PrClosed, now);
                await db.TransitionAsync(reviewQueuedIssue, failed, domainEventDispatcher, cancellationToken);
                break;
            }

            case ReviewInProgressIssue reviewInProgressIssue:
            {
                ContinuableFailedIssue failed = reviewInProgressIssue.Fail(FailureReason, FailureCategory.PrClosed, now);
                await db.TransitionAsync(reviewInProgressIssue, failed, domainEventDispatcher, cancellationToken);
                break;
            }

            case ReviewFailedIssue reviewFailedIssue:
            {
                ContinuableFailedIssue failed = reviewFailedIssue.Fail(FailureReason, FailureCategory.PrClosed, now);
                await db.TransitionAsync(reviewFailedIssue, failed, domainEventDispatcher, cancellationToken);
                break;
            }

            case AwaitingInputIssue { ResumeTarget: ResumeTarget.Revision } awaitingInputIssue:
            {
                ContinuableFailedIssue failed = awaitingInputIssue.Fail(FailureReason, FailureCategory.PrClosed, now);
                await db.TransitionAsync(awaitingInputIssue, failed, domainEventDispatcher, cancellationToken);
                break;
            }

            default:
                logger.LogWarning(
                    "ProviderPullRequestClosed received for repository {RepositoryId} issue {IssueNumber} but it is not in review, a review step, checks pending, rebase pending, merge pending or awaiting input on a pull request (state: {State}); ignoring.",
                    @event.RepositoryId,
                    @event.IssueNumber,
                    issue?.GetType().Name ?? "not found");
                break;
        }
    }
}
