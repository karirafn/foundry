using System.Diagnostics;

using Foundry.Modules.Issues.Features.Claiming;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;

using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Issues.Features.WorkerReactions;

internal sealed class WorkerCapacityAvailableHandler(
    DispatchCandidateSelector selector,
    IssueClaimer claimer,
    ILogger<WorkerCapacityAvailableHandler> logger) : IIntegrationEventHandler<WorkerCapacityAvailable>
{
    public async Task HandleAsync(WorkerCapacityAvailable @event, CancellationToken cancellationToken)
    {
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
                break;

            case SelectionOutcome.NoCandidates:
                logger.LogDebug("No claimable candidates found; skipping dispatch.");
                break;

            case SelectionOutcome.AllCandidatesUnresolvable(int skipped):
                logger.LogWarning(
                    "All {Skipped} candidate(s) skipped because their repository dispatch info could not be resolved.",
                    skipped);
                break;

            default:
                throw new UnreachableException($"Unhandled SelectionOutcome: {outcome.GetType().Name}");
        }
    }
}
