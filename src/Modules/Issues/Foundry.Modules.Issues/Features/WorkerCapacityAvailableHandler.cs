using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Contracts.Queries;
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
    IRepositoryEligibilityQuery repositoryEligibilityQuery,
    IDomainEventDispatcher domainEventDispatcher,
    IAuthValidator authValidator,
    ISystemNotificationBroadcaster systemNotificationBroadcaster,
    ILogger<WorkerCapacityAvailableHandler> logger) : IIntegrationEventHandler<WorkerCapacityAvailable>
{
    private const string ClaudeAuthCategory = "claude-auth";

    public async Task HandleAsync(WorkerCapacityAvailable @event, CancellationToken cancellationToken)
    {
        AuthValidationResult authResult = await authValidator.ValidateAsync(cancellationToken);

        if (!authResult.IsValid)
        {
            await systemNotificationBroadcaster.SendAsync(
                new SystemNotification(ClaudeAuthCategory, true, authResult.ErrorMessage ?? string.Empty),
                cancellationToken);
            return;
        }

        await systemNotificationBroadcaster.SendAsync(
            new SystemNotification(ClaudeAuthCategory, false, ""),
            cancellationToken);

        // Claim priority: revision queued first (addressing review feedback takes precedence),
        // then continuation queued (resuming interrupted work), then fresh queued issues.
        RevisionQueuedIssue? revisionQueued = await db.Set<RevisionQueuedIssue>()
            .OrderBy(i => i.DetectedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (revisionQueued is not null)
        {
            bool eligible = await IsRepositoryEligibleAsync([revisionQueued.MonitoredRepositoryId], cancellationToken);
            if (!eligible)
            {
                return;
            }

            await ClaimRevisionQueuedAsync(revisionQueued, @event.WorkerRunId, cancellationToken);
            return;
        }

        ContinuationQueuedIssue? continuationQueued = await db.Set<ContinuationQueuedIssue>()
            .OrderBy(i => i.DetectedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (continuationQueued is not null)
        {
            bool eligible = await IsRepositoryEligibleAsync([continuationQueued.MonitoredRepositoryId], cancellationToken);
            if (!eligible)
            {
                return;
            }

            await ClaimContinuationQueuedAsync(continuationQueued, @event.WorkerRunId, cancellationToken);
            return;
        }

        QueuedIssue? queued = await db.Set<QueuedIssue>()
            .OrderBy(i => i.DetectedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (queued is null)
        {
            return;
        }

        bool queuedEligible = await IsRepositoryEligibleAsync([queued.MonitoredRepositoryId], cancellationToken);
        if (!queuedEligible)
        {
            return;
        }

        await ClaimQueuedAsync(queued, @event.WorkerRunId, cancellationToken);
    }

    private async Task<bool> IsRepositoryEligibleAsync(
        IReadOnlyCollection<MonitoredRepositoryId> repositoryIds,
        CancellationToken cancellationToken)
    {
        IReadOnlyCollection<Guid> rawIds = repositoryIds
            .Select(id => id.Value)
            .ToList();

        IReadOnlySet<Guid> eligibleIds = await repositoryEligibilityQuery
            .GetEligibleRepositoryIdsAsync(rawIds, cancellationToken);

        return rawIds.All(eligibleIds.Contains);
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
        await db.TransitionAsync(revisionQueued, revisionInProgress, domainEventDispatcher, cancellationToken);

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
            dispatchInfo.AccountToken,
            revisionQueued.BranchName,
            revisionQueued.MonitoredRepositoryId,
            revision);

        await integrationEventDispatcher.DispatchAsync(
            [new IssueClaimed(dispatch)],
            cancellationToken);
    }

    private async Task ClaimContinuationQueuedAsync(
        ContinuationQueuedIssue continuationQueued,
        Guid workerRunId,
        CancellationToken cancellationToken)
    {
        RepositoryDispatchInfo? dispatchInfo = await repositoryDispatchQueries.GetDispatchInfoAsync(
            continuationQueued.MonitoredRepositoryId,
            cancellationToken);

        if (dispatchInfo is null)
        {
            logger.LogWarning(
                "Could not find dispatch info for repository {RepositoryId}; continuation issue #{IssueNumber} not claimed.",
                continuationQueued.MonitoredRepositoryId,
                continuationQueued.IssueNumber);
            return;
        }

        InProgressIssue inProgress = continuationQueued.Claim(workerRunId);
        await db.TransitionAsync(continuationQueued, inProgress, domainEventDispatcher, cancellationToken);

        ContinuationContext continuation = new(continuationQueued.BranchName);

        ClaimedIssueDispatch dispatch = new(
            inProgress.Id,
            workerRunId,
            inProgress.IssueNumber,
            inProgress.Title,
            inProgress.Body,
            dispatchInfo.RepositorySlug,
            dispatchInfo.CloneUrl,
            dispatchInfo.AccountToken,
            continuationQueued.BranchName,
            continuationQueued.MonitoredRepositoryId,
            Continuation: continuation);

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
        await db.TransitionAsync(queued, inProgress, domainEventDispatcher, cancellationToken);

        string branchName = BranchName.Generate(queued.IssueKind.BranchPrefix, queued.IssueNumber, queued.Title).Value;

        ClaimedIssueDispatch dispatch = new(
            inProgress.Id,
            workerRunId,
            inProgress.IssueNumber,
            inProgress.Title,
            inProgress.Body,
            dispatchInfo.RepositorySlug,
            dispatchInfo.CloneUrl,
            dispatchInfo.AccountToken,
            branchName,
            queued.MonitoredRepositoryId);

        await integrationEventDispatcher.DispatchAsync(
            [new IssueClaimed(dispatch)],
            cancellationToken);
    }
}
