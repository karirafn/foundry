using Foundry.Shared;

namespace Foundry.Modules.Workers.Contracts;

/// <summary>
/// A review run exited 0 with a verdict that passed every check against the run's supplied facts (ADR 0113, ADR 0114).
/// Published instead of <see cref="WorkerRunCompleted"/>: a review leaves no branch or merge request. Code derived
/// <paramref name="Outcome"/>; the model never returns one. A review without a valid verdict fails through
/// <see cref="WorkerRunFailed"/> instead.
/// </summary>
/// <param name="ReviewedCommit">The commit the reviewer judged.</param>
/// <param name="PatchId">The stable patch-id of the reviewed diff; <see langword="null"/> for a diff with no changes.</param>
/// <param name="BlockingFindings">The verdict's blocking findings, in the verdict's order.</param>
/// <param name="UnmetCriteria">The ids of the criteria the reviewer judged unmet.</param>
/// <param name="DecidedAt">When Foundry derived the outcome.</param>
/// <param name="AdvisoryFindings">The verdict's advisory findings, in the verdict's order.</param>
/// <param name="UnmetCriterionDetails">The unmet criteria with their text and the reviewer's evidence.</param>
/// <param name="TestsReason">The reviewer's reason for <paramref name="TestsAssessment"/>.</param>
/// <remarks>
/// The last three are optional so an event enqueued before they existed still deserializes from the outbox; such an
/// event reads as carrying none.
/// </remarks>
public sealed record ReviewRunCompleted(
    Guid IssueId,
    WorkerRunId WorkerRunId,
    ReviewVerdictOutcome Outcome,
    string ReviewedCommit,
    string? PatchId,
    IReadOnlyList<ReviewFinding> BlockingFindings,
    IReadOnlyList<string> UnmetCriteria,
    ReviewTestsAssessment TestsAssessment,
    DateTimeOffset DecidedAt,
    IReadOnlyList<ReviewFinding>? AdvisoryFindings = null,
    IReadOnlyList<ReviewUnmetCriterion>? UnmetCriterionDetails = null,
    string? TestsReason = null) : IIntegrationEvent;
