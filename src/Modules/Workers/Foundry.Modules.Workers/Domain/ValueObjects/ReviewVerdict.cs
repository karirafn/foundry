using Foundry.Modules.Workers.Contracts;

namespace Foundry.Modules.Workers.Domain.ValueObjects;

/// <summary>
/// The review role's typed verdict (ADR 0113), mirroring <c>workers/roles/review/verdict.schema.json</c>. It carries no
/// outcome: code checks it against the run's <see cref="ReviewFacts"/> and derives the outcome itself.
/// </summary>
public sealed record ReviewVerdict
{
    private static readonly ReviewCategory[] RuleBackedCategories =
        [ReviewCategory.Quality, ReviewCategory.Design, ReviewCategory.Conventions];

    public ReviewVerdict(
        string summary,
        IReadOnlyList<ReviewFinding> findings,
        IReadOnlyList<ReviewDimension> dimensions,
        IReadOnlyList<ReviewCriterionAnswer> criteria,
        ReviewTests tests)
    {
        ArgumentException.ThrowIfNullOrEmpty(summary);
        ArgumentNullException.ThrowIfNull(findings);
        ArgumentNullException.ThrowIfNull(dimensions);
        ArgumentNullException.ThrowIfNull(criteria);
        ArgumentNullException.ThrowIfNull(tests);

        ReviewCategory[] answered = [.. dimensions.Select(dimension => dimension.Category).Order()];

        if (!answered.SequenceEqual(Enum.GetValues<ReviewCategory>()))
        {
            throw new ArgumentException("A review verdict answers every dimension exactly once.", nameof(dimensions));
        }

        Summary = summary;
        Findings = [.. findings];
        Dimensions = [.. dimensions.OrderBy(dimension => dimension.Category)];
        Criteria = [.. criteria];
        Tests = tests;
    }

    public string Summary { get; }

    public IReadOnlyList<ReviewFinding> Findings { get; }

    /// <summary>One answer per <see cref="ReviewCategory"/>, in the category order.</summary>
    public IReadOnlyList<ReviewDimension> Dimensions { get; }

    public IReadOnlyList<ReviewCriterionAnswer> Criteria { get; }

    public ReviewTests Tests { get; }

    public IEnumerable<ReviewFinding> BlockingFindings =>
        Findings.Where(finding => finding.Severity == ReviewFindingSeverity.Blocking);

    public IEnumerable<ReviewCriterionAnswer> UnmetCriteria =>
        Criteria.Where(criterion => criterion.Status == ReviewCriterionStatus.Unmet);

    /// <summary>
    /// Why this verdict cannot stand against the run's <paramref name="facts"/>, or <see langword="null"/> when it can.
    /// <paramref name="principleIds"/> are the principle ids the run's role carries: the only ones a rule can cite.
    /// </summary>
    public FailureReason.VerdictInvalid? RejectionAgainst(ReviewFacts facts, IReadOnlyCollection<string> principleIds)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(principleIds);

        return DimensionRejection()
            ?? RuleRejection(principleIds)
            ?? CriteriaRejection(facts.Criteria)
            ?? TestsRejection(facts);
    }

    /// <summary>
    /// The outcome of a verdict that passed <see cref="RejectionAgainst"/>: changes are requested when anything blocks,
    /// a person must check when a criterion could not be verified or none were supplied, and otherwise it is approved.
    /// </summary>
    public ReviewVerdictOutcome OutcomeAgainst(ReviewFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);

        if (BlockingFindings.Any() || Tests.Assessment == ReviewTestsAssessment.Missing || UnmetCriteria.Any())
        {
            return ReviewVerdictOutcome.ChangesRequested;
        }

        if (facts.Criteria.Items.Count == 0
            || Criteria.Any(criterion => criterion.Status == ReviewCriterionStatus.NotVerifiable))
        {
            return ReviewVerdictOutcome.ApprovedForPerson;
        }

        return ReviewVerdictOutcome.Approved;
    }

    private FailureReason.VerdictInvalid? DimensionRejection()
    {
        foreach (ReviewDimension dimension in Dimensions)
        {
            string token = ReviewTokens.Category(dimension.Category);

            if (dimension.Status == ReviewDimensionStatus.Findings
                && !Findings.Any(finding => finding.Category == dimension.Category))
            {
                return new FailureReason.VerdictInvalid(
                    $"Review verdict answers the '{token}' dimension with findings but lists no {token} finding");
            }

            if (dimension.Status != ReviewDimensionStatus.Findings
                && BlockingFindings.Any(finding => finding.Category == dimension.Category))
            {
                return new FailureReason.VerdictInvalid(
                    $"Review verdict answers the '{token}' dimension as {ReviewTokens.DimensionStatus(dimension.Status)} but lists a blocking {token} finding");
            }
        }

        return null;
    }

    private FailureReason.VerdictInvalid? RuleRejection(IReadOnlyCollection<string> principleIds)
    {
        foreach (ReviewFinding finding in BlockingFindings)
        {
            string where = $"Review verdict's blocking {ReviewTokens.Category(finding.Category)} finding on '{finding.Path}'";

            if (finding.Rule is not { } rule || (rule.Principle is null && rule.File is null && rule.Quote is null))
            {
                if (RuleBackedCategories.Contains(finding.Category))
                {
                    return new FailureReason.VerdictInvalid($"{where} cites no rule");
                }

                continue;
            }

            bool citesPrinciple = rule.Principle is not null;
            bool citesFile = rule.File is not null || rule.Quote is not null;

            if (citesPrinciple && citesFile)
            {
                return new FailureReason.VerdictInvalid($"{where} cites both a principle and a convention file");
            }

            if (citesPrinciple && !principleIds.Contains(rule.Principle!))
            {
                return new FailureReason.VerdictInvalid($"{where} cites '{rule.Principle}', which is not a principle id of the run's role");
            }

            if (citesFile && (string.IsNullOrWhiteSpace(rule.File) || string.IsNullOrWhiteSpace(rule.Quote)))
            {
                return new FailureReason.VerdictInvalid($"{where} cites a convention file without both the file and a quote");
            }
        }

        return null;
    }

    private FailureReason.VerdictInvalid? CriteriaRejection(AcceptanceCriteria supplied)
    {
        string[] answered = [.. Criteria.Select(criterion => criterion.Id).Order(StringComparer.Ordinal)];
        string[] expected = [.. supplied.Items.Select(criterion => criterion.Id).Order(StringComparer.Ordinal)];

        if (answered.SequenceEqual(expected, StringComparer.Ordinal))
        {
            return null;
        }

        static string Listed(IEnumerable<string> ids) => ids.Any() ? string.Join(", ", ids) : "none";

        return new FailureReason.VerdictInvalid(
            $"Review verdict answers criteria [{Listed(Criteria.Select(criterion => criterion.Id))}] but the run supplied [{Listed(supplied.Items.Select(criterion => criterion.Id))}]");
    }

    private FailureReason.VerdictInvalid? TestsRejection(ReviewFacts facts)
    {
        if (!facts.ProductionChangedWithoutTests || Tests.Assessment != ReviewTestsAssessment.Adequate)
        {
            return null;
        }

        if (Tests.CoveringTests.Count == 0)
        {
            return new FailureReason.VerdictInvalid(
                "Review verdict judges the tests adequate for production changes with no test change but names no covering test");
        }

        foreach (string path in Tests.CoveringTests)
        {
            if (string.IsNullOrEmpty(path) || ChangedFileClassifier.Classify(path) != ChangedFileClass.Test)
            {
                return new FailureReason.VerdictInvalid(
                    $"Review verdict names '{path}' as a covering test, which is not a test path");
            }
        }

        return null;
    }

    public bool Equals(ReviewVerdict? other) =>
        other is not null
        && Summary == other.Summary
        && Findings.SequenceEqual(other.Findings)
        && Dimensions.SequenceEqual(other.Dimensions)
        && Criteria.SequenceEqual(other.Criteria)
        && Tests == other.Tests;

    public override int GetHashCode() => HashCode.Combine(Summary, Findings.Count, Criteria.Count, Tests);
}

/// <summary>
/// A review verdict that passed its checks, with the outcome code derived from it and the commit and patch-id it
/// judged: what a completed review run stores.
/// </summary>
public sealed record ReviewDecision
{
    private ReviewDecision(ReviewVerdict verdict, ReviewVerdictOutcome outcome, CommitSha reviewedCommit, PatchId? patchId)
    {
        Verdict = verdict;
        Outcome = outcome;
        ReviewedCommit = reviewedCommit;
        PatchId = patchId;
    }

    public ReviewVerdict Verdict { get; }

    public ReviewVerdictOutcome Outcome { get; }

    public CommitSha ReviewedCommit { get; }

    public PatchId? PatchId { get; }

    /// <summary>
    /// Checks <paramref name="verdict"/> against <paramref name="facts"/> and decides it, or returns why it cannot stand.
    /// </summary>
    public static ReviewDecisionResult Decide(ReviewVerdict verdict, ReviewFacts facts, IReadOnlyCollection<string> principleIds)
    {
        ArgumentNullException.ThrowIfNull(verdict);
        ArgumentNullException.ThrowIfNull(facts);

        return verdict.RejectionAgainst(facts, principleIds) is { } rejection
            ? new ReviewDecisionResult.Rejected(rejection)
            : new ReviewDecisionResult.Decided(
                new ReviewDecision(verdict, verdict.OutcomeAgainst(facts), facts.ReviewedCommit, facts.PatchId));
    }

    /// <summary>Rehydrates a decision stored on a run.</summary>
    internal static ReviewDecision FromStored(
        ReviewVerdict verdict,
        ReviewVerdictOutcome outcome,
        CommitSha reviewedCommit,
        PatchId? patchId) =>
        new(verdict, outcome, reviewedCommit, patchId);
}

/// <summary>A review verdict checked against its facts: decided, or rejected with the reason.</summary>
public abstract record ReviewDecisionResult
{
    private ReviewDecisionResult()
    {
    }

    public sealed record Decided(ReviewDecision Decision) : ReviewDecisionResult;

    public sealed record Rejected(FailureReason.VerdictInvalid Reason) : ReviewDecisionResult;
}

/// <summary>The review verdict schema's tokens, as the verdict and the stored decision spell them.</summary>
public static class ReviewTokens
{
    public static string Category(ReviewCategory category) => category switch
    {
        ReviewCategory.Correctness => "correctness",
        ReviewCategory.Criteria => "criteria",
        ReviewCategory.Tests => "tests",
        ReviewCategory.Security => "security",
        ReviewCategory.Quality => "quality",
        ReviewCategory.Design => "design",
        ReviewCategory.Conventions => "conventions",
        _ => throw new ArgumentOutOfRangeException(nameof(category), category, "Unknown review category."),
    };

    public static string DimensionStatus(ReviewDimensionStatus status) => status switch
    {
        ReviewDimensionStatus.Clear => "clear",
        ReviewDimensionStatus.Findings => "findings",
        ReviewDimensionStatus.NotApplicable => "not_applicable",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown dimension status."),
    };

    public static string Severity(ReviewFindingSeverity severity) => severity switch
    {
        ReviewFindingSeverity.Blocking => "blocking",
        ReviewFindingSeverity.Advisory => "advisory",
        _ => throw new ArgumentOutOfRangeException(nameof(severity), severity, "Unknown finding severity."),
    };

    public static string CriterionStatus(ReviewCriterionStatus status) => status switch
    {
        ReviewCriterionStatus.Met => "met",
        ReviewCriterionStatus.Unmet => "unmet",
        ReviewCriterionStatus.NotVerifiable => "not_verifiable",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown criterion status."),
    };

    public static string TestsAssessment(ReviewTestsAssessment assessment) => assessment switch
    {
        ReviewTestsAssessment.Adequate => "adequate",
        ReviewTestsAssessment.Missing => "missing",
        ReviewTestsAssessment.NotNeeded => "not_needed",
        _ => throw new ArgumentOutOfRangeException(nameof(assessment), assessment, "Unknown tests assessment."),
    };

    public static string Outcome(ReviewVerdictOutcome outcome) => outcome switch
    {
        ReviewVerdictOutcome.Approved => "approved",
        ReviewVerdictOutcome.ApprovedForPerson => "approved_for_person",
        ReviewVerdictOutcome.ChangesRequested => "changes_requested",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Unknown review outcome."),
    };

    public static IReadOnlyDictionary<string, ReviewCategory> Categories { get; } = TokenMap<ReviewCategory>(Category);

    public static IReadOnlyDictionary<string, ReviewDimensionStatus> DimensionStatuses { get; } = TokenMap<ReviewDimensionStatus>(DimensionStatus);

    public static IReadOnlyDictionary<string, ReviewFindingSeverity> Severities { get; } = TokenMap<ReviewFindingSeverity>(Severity);

    public static IReadOnlyDictionary<string, ReviewCriterionStatus> CriterionStatuses { get; } = TokenMap<ReviewCriterionStatus>(CriterionStatus);

    public static IReadOnlyDictionary<string, ReviewTestsAssessment> TestsAssessments { get; } = TokenMap<ReviewTestsAssessment>(TestsAssessment);

    public static IReadOnlyDictionary<string, ReviewVerdictOutcome> Outcomes { get; } = TokenMap<ReviewVerdictOutcome>(Outcome);

    private static Dictionary<string, TEnum> TokenMap<TEnum>(Func<TEnum, string> token)
        where TEnum : struct, Enum =>
        Enum.GetValues<TEnum>().ToDictionary(token, value => value, StringComparer.Ordinal);
}
