using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Contracts.Events;
using Foundry.Modules.Issues.Contracts.Queries;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Contracts.Events;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Issues.Features.WorkerReactions;

internal sealed class WorkerCapacityAvailableHandler(
    DbContext db,
    IRepositoryDispatchQueries repositoryDispatchQueries,
    IIntegrationEventDispatcher integrationEventDispatcher,
    IRepositoryEligibilityQuery repositoryEligibilityQuery,
    IDomainEventDispatcher domainEventDispatcher,
    ILogger<WorkerCapacityAvailableHandler> logger) : IIntegrationEventHandler<WorkerCapacityAvailable>
{
    public async Task HandleAsync(WorkerCapacityAvailable @event, CancellationToken cancellationToken)
    {
        // Resolve eligible repositories (with position) once across all tiers to avoid blocking
        // dispatch when the oldest candidate belongs to an ineligible repository.
        Dictionary<MonitoredRepositoryId, int> positionByRepoId =
            await ResolveEligibleRepositoryPositionsAsync(cancellationToken);

        if (positionByRepoId.Count == 0)
        {
            return;
        }

        Issue? winner = await PickByMinDispatchOrderKeyAsync(positionByRepoId, cancellationToken);

        if (winner is null)
        {
            return;
        }

        switch (winner)
        {
            case RevisionQueuedIssue revisionQueued:
                await ClaimRevisionQueuedAsync(revisionQueued, @event.WorkerRunId, cancellationToken);
                break;
            case ContinuationQueuedIssue continuationQueued:
                await ClaimContinuationQueuedAsync(continuationQueued, @event.WorkerRunId, cancellationToken);
                break;
            case QueuedIssue queued:
                await ClaimQueuedAsync(queued, @event.WorkerRunId, cancellationToken);
                break;
        }
    }

    /// <summary>
    /// Loads all queued candidates from eligible repositories across all tiers, then returns
    /// the candidate with the minimum <see cref="DispatchOrderKey"/>. The key orders by
    /// TierRank → Position → DetectedAt → IssueId, which preserves the tier precedence
    /// (revision → continuation → fresh) while also breaking ties by repository position,
    /// DetectedAt, and finally IssueId.
    /// </summary>
    private async Task<Issue?> PickByMinDispatchOrderKeyAsync(
        Dictionary<MonitoredRepositoryId, int> positionByRepoId,
        CancellationToken cancellationToken)
    {
        Dictionary<MonitoredRepositoryId, int>.KeyCollection eligibleIds = positionByRepoId.Keys;

        List<Issue> candidates = await db.Set<Issue>()
            .Where(i => i is RevisionQueuedIssue || i is ContinuationQueuedIssue || i is QueuedIssue)
            .Where(i => eligibleIds.Contains(i.MonitoredRepositoryId))
            .ToListAsync(cancellationToken);

        return candidates
            .MinBy(i => DispatchOrderKey.For(i, positionByRepoId[i.MonitoredRepositoryId]));
    }

    private async Task<Dictionary<MonitoredRepositoryId, int>> ResolveEligibleRepositoryPositionsAsync(
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

        IReadOnlyList<EligibleRepository> eligibleRepos = await repositoryEligibilityQuery
            .GetEligibleRepositoriesAsync(rawIds, cancellationToken);

        return eligibleRepos
            .Select(r => (Id: MonitoredRepositoryId.From(r.Id), r.Position))
            .ToDictionary(r => r.Id, r => r.Position);
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

        if (WorkerProvider.FromDiscriminator(dispatchInfo.ProviderType) is not Result<WorkerProvider>.Success providerSuccess)
        {
            logger.LogWarning(
                "Unknown provider discriminator '{Discriminator}' for repository {RepositoryId}; revision issue #{IssueNumber} not claimed.",
                dispatchInfo.ProviderType,
                revisionQueued.MonitoredRepositoryId,
                revisionQueued.IssueNumber);
            return;
        }

        RevisionInProgressIssue revisionInProgress = revisionQueued.Claim(workerRunId);

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
            providerSuccess.Value,
            revision);

        await integrationEventDispatcher.DispatchAsync(
            [new IssueClaimed(dispatch)],
            cancellationToken);

        await db.TransitionAsync(revisionQueued, revisionInProgress, domainEventDispatcher, cancellationToken);
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

        if (WorkerProvider.FromDiscriminator(dispatchInfo.ProviderType) is not Result<WorkerProvider>.Success providerSuccess)
        {
            logger.LogWarning(
                "Unknown provider discriminator '{Discriminator}' for repository {RepositoryId}; continuation issue #{IssueNumber} not claimed.",
                dispatchInfo.ProviderType,
                continuationQueued.MonitoredRepositoryId,
                continuationQueued.IssueNumber);
            return;
        }

        InProgressIssue inProgress = continuationQueued.Claim(workerRunId);

        ContinuationContext continuation = new(continuationQueued.BranchName, continuationQueued.FailureReason);

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
            providerSuccess.Value,
            Continuation: continuation);

        await integrationEventDispatcher.DispatchAsync(
            [new IssueClaimed(dispatch)],
            cancellationToken);

        await db.TransitionAsync(continuationQueued, inProgress, domainEventDispatcher, cancellationToken);
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

        if (WorkerProvider.FromDiscriminator(dispatchInfo.ProviderType) is not Result<WorkerProvider>.Success providerSuccess)
        {
            logger.LogWarning(
                "Unknown provider discriminator '{Discriminator}' for repository {RepositoryId}; issue #{IssueNumber} not claimed.",
                dispatchInfo.ProviderType,
                queued.MonitoredRepositoryId,
                queued.IssueNumber);
            return;
        }

        InProgressIssue inProgress = queued.Claim(workerRunId);

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
            queued.MonitoredRepositoryId,
            providerSuccess.Value);

        await integrationEventDispatcher.DispatchAsync(
            [new IssueClaimed(dispatch)],
            cancellationToken);

        await db.TransitionAsync(queued, inProgress, domainEventDispatcher, cancellationToken);
    }
}
