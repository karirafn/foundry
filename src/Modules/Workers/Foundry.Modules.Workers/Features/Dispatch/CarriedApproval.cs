using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;

using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Workers.Features.Dispatch;

/// <summary>
/// Decides whether a claimed review can complete without a run (ADR 0113): the change prepared for it has the same
/// zero-context patch-id as the issue's latest approval, so the approval carries to the new head. CI still ran on that
/// head before the review was queued.
/// </summary>
internal static class CarriedApproval
{
    /// <summary>
    /// The issue's latest approval carried to the reviewed commit, or <see langword="null"/> when the review runs in
    /// full: the context carries no approved patch-id, the prepared change has no patch-id or a different one, or the
    /// issue's latest recorded review is not that approval.
    /// </summary>
    public static async Task<RecordedReviewVerdict?> FindAsync(
        DbContext dbContext,
        IssueId issueId,
        DispatchContext.Review context,
        SuppliedReviewFacts facts,
        CancellationToken cancellationToken)
    {
        if (context.CarriedPatchId is not { } carriedPatchId
            || facts.PatchId is not { } preparedPatchId
            || !string.Equals(preparedPatchId.Value, carriedPatchId, StringComparison.Ordinal))
        {
            return null;
        }

        // An issue has few review runs, and SQLite cannot order by the stored completion time, so they are ordered here.
        List<CompletedRun> reviews = await dbContext.Set<WorkerRun>()
            .OfType<CompletedRun>()
            .Where(run => run.IssueId == issueId && run.ReviewVerdict != null)
            .ToListAsync(cancellationToken);

        CompletedRun? latest = reviews.MaxBy(run => run.CompletedAt);

        return latest?.ReviewVerdict is
        {
            Outcome: ReviewVerdictOutcome.Approved or ReviewVerdictOutcome.ApprovedForPerson,
        } verdict
            && string.Equals(verdict.PatchId, carriedPatchId, StringComparison.Ordinal)
            ? verdict.CarryTo(facts.ReviewedCommit.Value, latest.Id)
            : null;
    }
}
