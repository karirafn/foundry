using Foundry.Modules.Workers.Contracts;

namespace Foundry.Modules.Issues.Domain.ValueObjects;

/// <summary>
/// The issue's most recent review verdict (ADR 0113): the review run, the outcome code derived from its verdict, and
/// the zero-context patch-id of the diff it judged (<see langword="null"/> for a diff with no changes).
/// </summary>
public sealed record LatestReview
{
    public LatestReview(WorkerRunId runId, ReviewVerdictOutcome outcome, string? patchId)
    {
        if (!Enum.IsDefined(outcome))
        {
            throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Unknown review verdict outcome.");
        }

        if (patchId is not null && string.IsNullOrWhiteSpace(patchId))
        {
            throw new ArgumentException("A patch-id is absent or non-blank.", nameof(patchId));
        }

        RunId = runId;
        Outcome = outcome;
        PatchId = patchId;
    }

    public WorkerRunId RunId { get; }

    public ReviewVerdictOutcome Outcome { get; }

    public string? PatchId { get; }

    /// <summary>
    /// The patch-id a later review of an unchanged change may carry this approval across, or <see langword="null"/>
    /// when there is nothing to carry: a request for changes is never carried, and a review without a patch-id cannot
    /// be matched.
    /// </summary>
    public string? CarriablePatchId =>
        Outcome is ReviewVerdictOutcome.Approved or ReviewVerdictOutcome.ApprovedForPerson ? PatchId : null;
}
