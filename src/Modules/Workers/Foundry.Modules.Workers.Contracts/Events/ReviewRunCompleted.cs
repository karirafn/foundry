using Foundry.Shared;

namespace Foundry.Modules.Workers.Contracts;

/// <summary>
/// A review run exited 0 with a verdict that passed every check against its supplied facts (ADR 0113, ADR 0114).
/// Published instead of <see cref="WorkerRunCompleted"/>: a review run leaves no branch or merge request. Code derived
/// <paramref name="Outcome"/>; the reviewer never returns it. A review run without a valid verdict fails through
/// <see cref="WorkerRunFailed"/> instead.
/// </summary>
/// <param name="ReviewedCommit">The commit the run reviewed.</param>
/// <param name="PatchId">The reviewed diff's patch-id; <see langword="null"/> when the diff changed nothing.</param>
/// <param name="BlockingFindings">The verdict's blocking findings; empty when none block.</param>
/// <param name="UnmetCriteria">The criteria the verdict answered <c>unmet</c>.</param>
/// <param name="DecidedAt">When Foundry derived the outcome.</param>
public sealed record ReviewRunCompleted(
    Guid IssueId,
    WorkerRunId WorkerRunId,
    ReviewVerdictOutcome Outcome,
    string ReviewedCommit,
    string? PatchId,
    IReadOnlyList<ReviewFinding> BlockingFindings,
    IReadOnlyList<ReviewCriterionAnswer> UnmetCriteria,
    ReviewTestsAssessment TestsAssessment,
    DateTimeOffset DecidedAt) : IIntegrationEvent;
