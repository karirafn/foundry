using System.Diagnostics;

using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Features.Claiming;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Issues.Features.WorkerReactions;

internal sealed class WorkerCapacityAvailableHandler(
    DbContext dbContext,
    DispatchCandidateSelector selector,
    IssueClaimer claimer,
    IIntegrationEventDispatcher integrationEventDispatcher,
    ILogger<WorkerCapacityAvailableHandler> logger) : IIntegrationEventHandler<WorkerCapacityAvailable>
{
    public async Task HandleAsync(WorkerCapacityAvailable @event, CancellationToken cancellationToken)
    {
        bool alreadyClaimed = await WorkerRunIdExistsAsync(@event.WorkerRunId, cancellationToken);

        if (alreadyClaimed)
        {
            logger.LogDebug(
                "Worker run {WorkerRunId} is already recorded on an issue; skipping dispatch.",
                @event.WorkerRunId);
            return;
        }

        SelectionOutcome outcome = await selector.SelectAsync(cancellationToken);

        switch (outcome)
        {
            case SelectionOutcome.Selected(DispatchCandidate candidate):
                await claimer.ClaimAsync(candidate, @event.WorkerRunId, cancellationToken);
                logger.LogInformation(
                    "Claimed issue #{IssueNumber} (tier {TierRank}) from {RepositorySlug} for worker run {WorkerRunId}.",
                    candidate.Issue.IssueNumber,
                    candidate.Issue.TierRank,
                    candidate.DispatchInfo.RepositorySlug,
                    @event.WorkerRunId);
                break;

            case SelectionOutcome.NoEligibleRepositories:
                logger.LogDebug("No eligible repositories found; skipping dispatch.");
                await integrationEventDispatcher.DispatchAsync(
                    [new ClaimSkipped(@event.WorkerRunId)],
                    cancellationToken);
                break;

            case SelectionOutcome.NoCandidates:
                logger.LogDebug("No claimable candidates found; skipping dispatch.");
                await integrationEventDispatcher.DispatchAsync(
                    [new ClaimSkipped(@event.WorkerRunId)],
                    cancellationToken);
                break;

            case SelectionOutcome.AllCandidatesUnresolvable(int skipped):
                logger.LogWarning(
                    "All {Skipped} candidate(s) skipped because their repository dispatch info could not be resolved.",
                    skipped);
                await integrationEventDispatcher.DispatchAsync(
                    [new ClaimSkipped(@event.WorkerRunId)],
                    cancellationToken);
                break;

            default:
                throw new UnreachableException($"Unhandled SelectionOutcome: {outcome.GetType().Name}");
        }
    }

    /// <summary>
    /// Returns true when any issue already carries the given <paramref name="workerRunId"/>.
    /// Queries each concrete TPH state that maps the <c>worker_run_id</c> column individually,
    /// because <see cref="Microsoft.EntityFrameworkCore.EF.Property{TProperty}"/> cannot access
    /// a property that is not declared on the queried base type (<c>Issue</c>).
    /// Short-circuits on the first match.
    ///
    /// Run-carrying states (those that declare <c>WorkerRunId</c>):
    ///   <see cref="States.InProgressIssue"/>, <see cref="States.RevisionInProgressIssue"/>,
    ///   <see cref="States.ReviewIssue"/>, <see cref="States.UnchangedIssue"/>,
    ///   <see cref="States.FailedIssue"/>, <see cref="States.ContinuableFailedIssue"/>,
    ///   <see cref="States.RevisionFailedIssue"/>.
    /// When adding a new <see cref="Domain.Entities.Issue"/> subtype that carries <c>WorkerRunId</c>,
    /// add a corresponding <c>AnyAsync</c> check here and update
    /// <c>WorkerRunIdGuardExhaustivenessTests.GuardedTypes</c> in the unit-test project.
    /// </summary>
    private async Task<bool> WorkerRunIdExistsAsync(
        WorkerRunId workerRunId,
        CancellationToken cancellationToken)
    {
        if (await dbContext.Set<InProgressIssue>()
                .AnyAsync(i => i.WorkerRunId == workerRunId, cancellationToken))
        {
            return true;
        }

        if (await dbContext.Set<RevisionInProgressIssue>()
                .AnyAsync(i => i.WorkerRunId == workerRunId, cancellationToken))
        {
            return true;
        }

        if (await dbContext.Set<ReviewIssue>()
                .AnyAsync(i => i.WorkerRunId == workerRunId, cancellationToken))
        {
            return true;
        }

        if (await dbContext.Set<UnchangedIssue>()
                .AnyAsync(i => i.WorkerRunId == workerRunId, cancellationToken))
        {
            return true;
        }

        if (await dbContext.Set<FailedIssue>()
                .AnyAsync(i => i.WorkerRunId == workerRunId, cancellationToken))
        {
            return true;
        }

        if (await dbContext.Set<ContinuableFailedIssue>()
                .AnyAsync(i => i.WorkerRunId == workerRunId, cancellationToken))
        {
            return true;
        }

        return await dbContext.Set<RevisionFailedIssue>()
            .AnyAsync(i => i.WorkerRunId == workerRunId, cancellationToken);
    }
}
