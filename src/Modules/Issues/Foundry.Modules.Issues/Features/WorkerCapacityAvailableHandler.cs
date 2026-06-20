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

        // Resolve eligible repository IDs once across all tiers to avoid blocking dispatch
        // when the oldest candidate belongs to an ineligible repository.
        HashSet<MonitoredRepositoryId> eligibleRepoIds = await ResolveEligibleRepositoryIdsAsync(cancellationToken);

        // Claim priority: revision queued first (addressing review feedback takes precedence),
        // then continuation queued (resuming interrupted work), then fresh queued issues.
        // Each tier skips ineligible repositories by filtering at the database level.
        RevisionQueuedIssue? revisionQueued = await db.Set<RevisionQueuedIssue>()
            .Where(i => eligibleRepoIds.Contains(i.MonitoredRepositoryId))
            .OrderBy(i => i.DetectedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (revisionQueued is not null)
        {
            await ClaimRevisionQueuedAsync(revisionQueued, @event.WorkerRunId, cancellationToken);
            return;
        }

        ContinuationQueuedIssue? continuationQueued = await db.Set<ContinuationQueuedIssue>()
            .Where(i => eligibleRepoIds.Contains(i.MonitoredRepositoryId))
            .OrderBy(i => i.DetectedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (continuationQueued is not null)
        {
            await ClaimContinuationQueuedAsync(continuationQueued, @event.WorkerRunId, cancellationToken);
            return;
        }

        QueuedIssue? queued = await db.Set<QueuedIssue>()
            .Where(i => eligibleRepoIds.Contains(i.MonitoredRepositoryId))
            .OrderBy(i => i.DetectedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (queued is not null)
        {
            await ClaimQueuedAsync(queued, @event.WorkerRunId, cancellationToken);
        }
    }

    private async Task<HashSet<MonitoredRepositoryId>> ResolveEligibleRepositoryIdsAsync(
        CancellationToken cancellationToken)
    {
        List<MonitoredRepositoryId> candidateRepoIds = await db.Set<Issue>()
            .Where(i => i is RevisionQueuedIssue || i is ContinuationQueuedIssue || i is QueuedIssue)
            .Select(i => i.MonitoredRepositoryId)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (candidateRepoIds.Count == 0)
        {
            return [];
        }

        List<Guid> rawIds = candidateRepoIds
            .Select(id => id.Value)
            .ToList();

        IReadOnlySet<Guid> eligibleRawIds = await repositoryEligibilityQuery
            .GetEligibleRepositoryIdsAsync(rawIds, cancellationToken);

        return candidateRepoIds
            .Where(id => eligibleRawIds.Contains(id.Value))
            .ToHashSet();
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
