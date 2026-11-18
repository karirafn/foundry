namespace Foundry.Modules.Workers.Contracts;

/// <summary>
/// What code decided from a valid review verdict (ADR 0113). Named apart from the Issues-side review outcome: this is
/// the verdict's outcome, which the issue lifecycle acts on.
/// </summary>
public enum ReviewVerdictOutcome
{
    /// <summary>Every criterion is met, no finding blocks, and the tests are not missing.</summary>
    Approved,

    /// <summary>Nothing blocks, but a criterion could not be verified or the issue listed none: a person should check.</summary>
    ApprovedForPerson,

    /// <summary>A finding blocks, the tests are missing, or a criterion is unmet.</summary>
    ChangesRequested,
}

/// <summary>Whether a review finding must be fixed before the change merges.</summary>
public enum ReviewFindingSeverity
{
    Blocking,
    Advisory,
}

/// <summary>The dimension of a review a finding belongs to; the verdict answers every one.</summary>
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
/// The rule a review finding applies: a principle id (<c>design.1</c>), or a repository convention file with a verbatim
/// quote from it. As the reviewer returned it, so any part may be missing.
/// </summary>
public sealed record ReviewRule(string? Principle, string? File, string? Quote);

/// <summary>One problem a reviewer found.</summary>
public sealed record ReviewFinding(
    string Path,
    int? Line,
    ReviewFindingSeverity Severity,
    ReviewCategory Category,
    ReviewRule? Rule,
    string Body);

/// <summary>A reviewer's answer for one dimension, with what it checked.</summary>
public sealed record ReviewDimension(ReviewCategory Category, ReviewDimensionStatus Status, string Note);

/// <summary>A reviewer's answer for one acceptance criterion, by its <c>AC</c> id.</summary>
public sealed record ReviewCriterionAnswer(string Id, ReviewCriterionStatus Status, string Evidence);

/// <summary>A reviewer's judgment of the change's tests, and the test paths it says cover the change.</summary>
public sealed record ReviewTests(ReviewTestsAssessment Assessment, string Reason, IReadOnlyList<string> CoveringTests)
{
    public bool Equals(ReviewTests? other) =>
        other is not null
        && Assessment == other.Assessment
        && Reason == other.Reason
        && CoveringTests.SequenceEqual(other.CoveringTests);

    public override int GetHashCode() => HashCode.Combine(Assessment, Reason, CoveringTests.Count);
}

/// <summary>
/// A review run's stored verdict (ADR 0113): the outcome code derived, the commit and patch-id reviewed, and the
/// verdict itself.
/// </summary>
public sealed record WorkerRunReviewVerdict(
    ReviewVerdictOutcome Outcome,
    string ReviewedCommit,
    string? PatchId,
    string Summary,
    IReadOnlyList<ReviewFinding> Findings,
    IReadOnlyList<ReviewDimension> Dimensions,
    IReadOnlyList<ReviewCriterionAnswer> Criteria,
    ReviewTests Tests);
