using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Issues.Features;

internal sealed class WorkerCapacityAvailableHandler(
    DbContext db,
    IRepositoryDispatchQueries repositoryDispatchQueries,
    IIntegrationEventDispatcher integrationEventDispatcher,
    IBranchProtectionValidator branchProtectionValidator,
    IDomainEventDispatcher domainEventDispatcher,
    ILogger<WorkerCapacityAvailableHandler> logger) : IIntegrationEventHandler<WorkerCapacityAvailable>
{
    public async Task HandleAsync(WorkerCapacityAvailable @event, CancellationToken cancellationToken)
    {
        // Revision queue takes priority — claim the oldest revision first, then fall back to fresh queue.
        RevisionQueuedIssue? revisionQueued = await db.Set<RevisionQueuedIssue>()
            .OrderBy(i => i.DetectedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (revisionQueued is not null)
        {
            await ClaimRevisionQueuedAsync(revisionQueued, @event.WorkerRunId, cancellationToken);
            return;
        }

        QueuedIssue? queued = await db.Set<QueuedIssue>()
            .OrderBy(i => i.DetectedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (queued is null)
        {
            return;
        }

        bool eligible = await ValidateAndMarkIneligibleIfNotAsync(queued, cancellationToken);
        if (!eligible)
        {
            return;
        }

        await ClaimQueuedAsync(queued, @event.WorkerRunId, cancellationToken);
    }

    private async Task<bool> ValidateAndMarkIneligibleIfNotAsync(
        QueuedIssue queued,
        CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<EligibilityViolationInfo>> validationResult =
            await branchProtectionValidator.ValidateAsync(queued.MonitoredRepositoryId, cancellationToken);

        List<EligibilityViolation> violations = validationResult switch
        {
            Result<IReadOnlyList<EligibilityViolationInfo>>.Success success when success.Value.Count > 0 =>
                success.Value
                    .Select(v => EligibilityViolation.From(v.Rule, v.Description))
                    .ToList(),
            Result<IReadOnlyList<EligibilityViolationInfo>>.Failure =>
                [EligibilityViolation.Unreachable("Branch protection check failed")],
            _ => [],
        };

        if (violations.Count == 0)
        {
            return true;
        }

        IneligibleIssue ineligible = queued.MarkIneligible(violations);
        await db.TransitionAsync(queued, ineligible, cancellationToken);
        await domainEventDispatcher.DispatchAsync(queued.DomainEvents, cancellationToken);

        logger.LogWarning(
            "Issue #{IssueNumber} in repository {RepositoryId} failed branch protection check; marked ineligible.",
            queued.IssueNumber,
            queued.MonitoredRepositoryId);

        return false;
    }

    private async Task ClaimRevisionQueuedAsync(
        RevisionQueuedIssue revisionQueued,
        Guid workerRunId,
        CancellationToken cancellationToken)
    {
        RepositoryDispatchInfo? dispatchInfo = await repositoryDispatchQueries.GetDispatchInfoAsync(
            revisionQueued.MonitoredRepositoryId,
            cancellationToken);

        if (dispatchInfo is null)
        {
            logger.LogWarning(
                "Could not find dispatch info for repository {RepositoryId}; revision issue #{IssueNumber} not claimed.",
                revisionQueued.MonitoredRepositoryId,
                revisionQueued.IssueNumber);
            return;
        }

        RevisionInProgressIssue revisionInProgress = revisionQueued.Claim(workerRunId);
        await db.TransitionAsync(revisionQueued, revisionInProgress, cancellationToken);

        RevisionContext revision = new(
            revisionQueued.BranchName,
            revisionQueued.PullRequestUrl,
            revisionQueued.ReviewComments);

        ClaimedIssueDispatch dispatch = new(
            revisionInProgress.Id,
            workerRunId,
            revisionInProgress.IssueNumber,
            revisionInProgress.Title,
            revisionInProgress.Body,
            dispatchInfo.RepositorySlug,
            dispatchInfo.CloneUrl,
            dispatchInfo.AccountSecretKeyName,
            revision);

        await integrationEventDispatcher.DispatchAsync(
            [new IssueClaimed(dispatch)],
            cancellationToken);
    }

    private async Task ClaimQueuedAsync(
        QueuedIssue queued,
        Guid workerRunId,
        CancellationToken cancellationToken)
    {
        RepositoryDispatchInfo? dispatchInfo = await repositoryDispatchQueries.GetDispatchInfoAsync(
            queued.MonitoredRepositoryId,
            cancellationToken);

        if (dispatchInfo is null)
        {
            logger.LogWarning(
                "Could not find dispatch info for repository {RepositoryId}; issue #{IssueNumber} not claimed.",
                queued.MonitoredRepositoryId,
                queued.IssueNumber);
            return;
        }

        InProgressIssue inProgress = queued.Claim(workerRunId);
        await db.TransitionAsync(queued, inProgress, cancellationToken);

        ClaimedIssueDispatch dispatch = new(
            inProgress.Id,
            workerRunId,
            inProgress.IssueNumber,
            inProgress.Title,
            inProgress.Body,
            dispatchInfo.RepositorySlug,
            dispatchInfo.CloneUrl,
            dispatchInfo.AccountSecretKeyName);

        await integrationEventDispatcher.DispatchAsync(
            [new IssueClaimed(dispatch)],
            cancellationToken);
    }
}
