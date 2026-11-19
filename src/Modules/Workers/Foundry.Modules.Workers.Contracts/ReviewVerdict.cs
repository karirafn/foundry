namespace Foundry.Modules.Workers.Contracts;

/// <summary>
/// What code decides from a valid review verdict (ADR 0113). The model never returns it: it is derived from the
/// verdict's findings, tests assessment and criteria. Named apart from the Issues module's <c>ReviewOutcome</c>.
/// </summary>
public enum ReviewVerdictOutcome
{
    /// <summary>No blocking finding, tests not missing, and every supplied criterion met.</summary>
    Approved,

    /// <summary>Nothing blocks, but a criterion could not be verified or none were supplied: a person should check.</summary>
    ApprovedForPerson,

    /// <summary>A blocking finding, missing tests, or an unmet criterion.</summary>
    ChangesRequested,
}

/// <summary>Whether a review finding blocks the merge.</summary>
public enum ReviewSeverity
{
    Blocking,
    Advisory,
}

/// <summary>The seven dimensions a review answers, each also a finding's category.</summary>
public enum ReviewCategory
{
    Correctness,
    Criteria,
    Tests,
    Security,
    Quality,
    Design,
    Conventions,
}

/// <summary>A reviewer's answer for one dimension.</summary>
public enum ReviewDimensionStatus
{
    Clear,
    Findings,
    NotApplicable,
}

/// <summary>A reviewer's answer for one acceptance criterion.</summary>
public enum ReviewCriterionStatus
{
    Met,
    Unmet,
    NotVerifiable,
}

/// <summary>A reviewer's judgment of the change's tests.</summary>
public enum ReviewTestsAssessment
{
    Adequate,
    Missing,
    NotNeeded,
}

/// <summary>
/// The rule a finding applies: a principle id (<see cref="Principle"/>), or a repository convention file with a
/// verbatim quote (<see cref="File"/> and <see cref="Quote"/>). A blocking quality, design or conventions finding
/// carries exactly one of the two forms; any other finding may carry either, both or a partial one.
/// </summary>
public sealed record ReviewRule(string? Principle, string? File, string? Quote);

/// <summary>One problem a reviewer found.</summary>
public sealed record ReviewFinding(
    string Path,
    int? Line,
    ReviewSeverity Severity,
    ReviewCategory Category,
    ReviewRule? Rule,
    string Body);

/// <summary>A reviewer's answer for one dimension.</summary>
public sealed record ReviewDimension(ReviewCategory Category, ReviewDimensionStatus Status, string Note);

/// <summary>A reviewer's answer for one supplied acceptance criterion, by its id (<c>AC1</c>…).</summary>
public sealed record ReviewCriterionAnswer(string Id, ReviewCriterionStatus Status, string Evidence);

/// <summary>
/// A supplied acceptance criterion the reviewer judged unmet: its id (<c>AC1</c>…), its text from the issue, and the
/// reviewer's evidence.
/// </summary>
public sealed record ReviewUnmetCriterion(string Id, string Text, string Evidence);

/// <summary>A reviewer's judgment of the change's tests, with the test files it says cover the change.</summary>
public sealed record ReviewTests(ReviewTestsAssessment Assessment, string Reason, IReadOnlyList<string> CoveringTests);

/// <summary>
/// The review a carried approval came from (ADR 0113): the run that judged the change, and the commit it judged.
/// </summary>
public sealed record ReviewCarry(WorkerRunId RunId, string ReviewedCommit);

/// <summary>
/// A valid review verdict as Foundry recorded it on its run: the verdict, the outcome code derived from it, and the
/// commit and patch-id it judged. <see cref="PatchId"/> is <see langword="null"/> for a diff with no changes.
/// <see cref="CarriedFrom"/> marks an approval carried to <see cref="ReviewedCommit"/> without a run, because the
/// change was unchanged since the review it names; <see langword="null"/> for a verdict a run returned.
/// </summary>
public sealed record RecordedReviewVerdict(
    string Summary,
    IReadOnlyList<ReviewFinding> Findings,
    IReadOnlyList<ReviewDimension> Dimensions,
    IReadOnlyList<ReviewCriterionAnswer> Criteria,
    ReviewTests Tests,
    ReviewVerdictOutcome Outcome,
    string ReviewedCommit,
    string? PatchId,
    ReviewCarry? CarriedFrom = null)
{
    /// <summary>
    /// Carries this approval to <paramref name="reviewedCommit"/>, whose change has the same patch-id. A verdict that
    /// was itself carried keeps naming the review that judged the change.
    /// </summary>
    /// <param name="reviewedCommit">The new head the approval now covers.</param>
    /// <param name="reviewRunId">The run that recorded this verdict.</param>
    public RecordedReviewVerdict CarryTo(string reviewedCommit, WorkerRunId reviewRunId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reviewedCommit);
        if (Outcome is not (ReviewVerdictOutcome.Approved or ReviewVerdictOutcome.ApprovedForPerson))
        {
            throw new InvalidOperationException("Only an approval carries to a new head; a request for changes is reviewed again.");
        }

        return this with
        {
            ReviewedCommit = reviewedCommit,
            CarriedFrom = CarriedFrom ?? new ReviewCarry(reviewRunId, ReviewedCommit),
        };
    }

    public bool Equals(RecordedReviewVerdict? other) =>
        other is not null
        && Summary == other.Summary
        && Findings.SequenceEqual(other.Findings)
        && Dimensions.SequenceEqual(other.Dimensions)
        && Criteria.SequenceEqual(other.Criteria)
        && Tests.Assessment == other.Tests.Assessment
        && Tests.Reason == other.Tests.Reason
        && Tests.CoveringTests.SequenceEqual(other.Tests.CoveringTests)
        && Outcome == other.Outcome
        && ReviewedCommit == other.ReviewedCommit
        && PatchId == other.PatchId
        && CarriedFrom == other.CarriedFrom;

    public override int GetHashCode() => HashCode.Combine(Summary, Outcome, ReviewedCommit, PatchId, Findings.Count);
}
