using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.ValueObjects;

namespace Foundry.Modules.Workers.Features.Verdicts;

/// <summary>
/// The review role's typed verdict, mirroring <c>workers/roles/review/verdict.schema.json</c> (ADR 0113): a summary,
/// findings, an answer for each of the seven dimensions, an answer per acceptance criterion, and a tests assessment.
/// It carries no outcome: code derives one once <see cref="CheckAgainst"/> accepts the verdict.
/// </summary>
internal sealed partial record ReviewVerdict(
    string Summary,
    IReadOnlyList<ReviewFinding> Findings,
    IReadOnlyList<ReviewDimension> Dimensions,
    IReadOnlyList<ReviewCriterionAnswer> Criteria,
    ReviewTests Tests)
{
    internal const string SummaryProperty = "summary";
    internal const string FindingsProperty = "findings";
    internal const string DimensionsProperty = "dimensions";
    internal const string CriteriaProperty = "criteria";
    internal const string TestsProperty = "tests";

    internal const string PathProperty = "path";
    internal const string LineProperty = "line";
    internal const string SeverityProperty = "severity";
    internal const string CategoryProperty = "category";
    internal const string RuleProperty = "rule";
    internal const string BodyProperty = "body";

    internal const string PrincipleProperty = "principle";
    internal const string FileProperty = "file";
    internal const string QuoteProperty = "quote";

    internal const string StatusProperty = "status";
    internal const string NoteProperty = "note";

    internal const string IdProperty = "id";
    internal const string EvidenceProperty = "evidence";

    internal const string AssessmentProperty = "assessment";
    internal const string ReasonProperty = "reason";
    internal const string CoveringTestsProperty = "coveringTests";

    internal const string CriterionIdPattern = "^AC[0-9]+$";

    internal static readonly string[] TopLevelProperties =
        [SummaryProperty, FindingsProperty, DimensionsProperty, CriteriaProperty, TestsProperty];

    internal static readonly string[] FindingProperties =
        [PathProperty, LineProperty, SeverityProperty, CategoryProperty, RuleProperty, BodyProperty];

    internal static readonly string[] RuleProperties = [PrincipleProperty, FileProperty, QuoteProperty];

    internal static readonly string[] DimensionProperties = [StatusProperty, NoteProperty];

    internal static readonly string[] CriterionProperties = [IdProperty, StatusProperty, EvidenceProperty];

    internal static readonly string[] TestsProperties = [AssessmentProperty, ReasonProperty, CoveringTestsProperty];

    /// <summary>The schema's <c>severity</c> tokens.</summary>
    internal static readonly IReadOnlyDictionary<string, ReviewSeverity> SeverityTokens =
        new Dictionary<string, ReviewSeverity>(StringComparer.Ordinal)
        {
            ["blocking"] = ReviewSeverity.Blocking,
            ["advisory"] = ReviewSeverity.Advisory,
        };

    /// <summary>The schema's <c>category</c> tokens, which are also the <c>dimensions</c> keys, in the schema's order.</summary>
    internal static readonly IReadOnlyList<KeyValuePair<string, ReviewCategory>> CategoryTokens =
    [
        new("correctness", ReviewCategory.Correctness),
        new("criteria", ReviewCategory.Criteria),
        new("tests", ReviewCategory.Tests),
        new("security", ReviewCategory.Security),
        new("quality", ReviewCategory.Quality),
        new("design", ReviewCategory.Design),
        new("conventions", ReviewCategory.Conventions),
    ];

    /// <summary>The schema's dimension <c>status</c> tokens.</summary>
    internal static readonly IReadOnlyDictionary<string, ReviewDimensionStatus> DimensionStatusTokens =
        new Dictionary<string, ReviewDimensionStatus>(StringComparer.Ordinal)
        {
            ["clear"] = ReviewDimensionStatus.Clear,
            ["findings"] = ReviewDimensionStatus.Findings,
            ["not_applicable"] = ReviewDimensionStatus.NotApplicable,
        };

    /// <summary>The schema's criterion <c>status</c> tokens.</summary>
    internal static readonly IReadOnlyDictionary<string, ReviewCriterionStatus> CriterionStatusTokens =
        new Dictionary<string, ReviewCriterionStatus>(StringComparer.Ordinal)
        {
            ["met"] = ReviewCriterionStatus.Met,
            ["unmet"] = ReviewCriterionStatus.Unmet,
            ["not_verifiable"] = ReviewCriterionStatus.NotVerifiable,
        };

    /// <summary>The schema's tests <c>assessment</c> tokens.</summary>
    internal static readonly IReadOnlyDictionary<string, ReviewTestsAssessment> AssessmentTokens =
        new Dictionary<string, ReviewTestsAssessment>(StringComparer.Ordinal)
        {
            ["adequate"] = ReviewTestsAssessment.Adequate,
            ["missing"] = ReviewTestsAssessment.Missing,
            ["not_needed"] = ReviewTestsAssessment.NotNeeded,
        };

    // A blocking finding in these categories is a matter of taste unless it cites a rule (ADR 0113).
    private static readonly HashSet<ReviewCategory> RuleBackedCategories =
        [ReviewCategory.Quality, ReviewCategory.Design, ReviewCategory.Conventions];

    /// <summary>
    /// Returns the verdict, or <see langword="null"/> when <paramref name="output"/> breaks the schema's shape: an
    /// unknown or missing property, a wrong type, or a token outside its enum.
    /// </summary>
    public static ReviewVerdict? TryParse(JsonObject output)
    {
        ArgumentNullException.ThrowIfNull(output);

        if (!HasExactly(output, TopLevelProperties, TopLevelProperties)
            || VerdictQuestionsReader.ReadString(output[SummaryProperty]) is not { Length: > 0 } summary
            || ReadFindings(output[FindingsProperty]) is not { } findings
            || ReadDimensions(output[DimensionsProperty]) is not { } dimensions
            || ReadCriteria(output[CriteriaProperty]) is not { } criteria
            || ReadTests(output[TestsProperty]) is not { } tests)
        {
            return null;
        }

        return new ReviewVerdict(summary, findings, dimensions, criteria, tests);
    }

    /// <summary>
    /// Checks the verdict against the facts the run was supplied and the principle ids its role could cite, and on
    /// success derives its outcome. The first broken rule rejects it as <c>verdict_invalid</c>.
    /// </summary>
    public ReviewVerdictCheck CheckAgainst(SuppliedReviewFacts facts, IReadOnlySet<string> principleIds) =>
        Valid.Check(this, facts, principleIds);

    public bool Equals(ReviewVerdict? other) =>
        other is not null
        && Summary == other.Summary
        && Findings.SequenceEqual(other.Findings)
        && Dimensions.SequenceEqual(other.Dimensions)
        && Criteria.SequenceEqual(other.Criteria)
        && Tests.Assessment == other.Tests.Assessment
        && Tests.Reason == other.Tests.Reason
        && Tests.CoveringTests.SequenceEqual(other.Tests.CoveringTests);

    public override int GetHashCode() => HashCode.Combine(Summary, Findings.Count, Criteria.Count, Tests.Assessment);

    /// <summary>A verdict that passed every check against its run's facts, with the outcome code derived from it.</summary>
    internal sealed record Valid
    {
        private Valid(ReviewVerdict verdict, SuppliedReviewFacts facts, ReviewVerdictOutcome outcome)
        {
            Verdict = verdict;
            Facts = facts;
            Outcome = outcome;
        }

        public ReviewVerdict Verdict { get; }

        public SuppliedReviewFacts Facts { get; }

        public ReviewVerdictOutcome Outcome { get; }

        /// <summary>The verdict's blocking findings, in its order.</summary>
        public IReadOnlyList<ReviewFinding> BlockingFindings =>
            [.. Verdict.Findings.Where(finding => finding.Severity == ReviewSeverity.Blocking)];

        /// <summary>The ids of the criteria judged unmet, in the verdict's order.</summary>
        public IReadOnlyList<string> UnmetCriteria =>
            [.. Verdict.Criteria.Where(answer => answer.Status == ReviewCriterionStatus.Unmet).Select(answer => answer.Id)];

        /// <summary>The verdict's advisory findings, in its order.</summary>
        public IReadOnlyList<ReviewFinding> AdvisoryFindings =>
            [.. Verdict.Findings.Where(finding => finding.Severity == ReviewSeverity.Advisory)];

        /// <summary>
        /// The criteria judged unmet, in the verdict's order, each with its supplied text and the reviewer's evidence.
        /// A valid verdict answers exactly the supplied ids, so every unmet id has its text.
        /// </summary>
        public IReadOnlyList<ReviewUnmetCriterion> UnmetCriterionDetails =>
            [.. Verdict.Criteria
                .Where(answer => answer.Status == ReviewCriterionStatus.Unmet)
                .Select(answer => new ReviewUnmetCriterion(
                    answer.Id,
                    Facts.Criteria.Single(criterion => criterion.Id == answer.Id).Text,
                    answer.Evidence))];

        /// <summary>The verdict as its run records it: with its outcome and the commit and patch-id it judged.</summary>
        public RecordedReviewVerdict ToRecorded() =>
            new(
                Verdict.Summary,
                Verdict.Findings,
                Verdict.Dimensions,
                Verdict.Criteria,
                Verdict.Tests,
                Outcome,
                Facts.ReviewedCommit.Value,
                Facts.PatchId?.Value);

        internal static ReviewVerdictCheck Check(
            ReviewVerdict verdict,
            SuppliedReviewFacts facts,
            IReadOnlySet<string> principleIds)
        {
            ArgumentNullException.ThrowIfNull(facts);
            ArgumentNullException.ThrowIfNull(principleIds);

            if (DimensionRejection(verdict) is { } dimension)
            {
                return new ReviewVerdictCheck.Rejected(dimension);
            }

            if (RuleRejection(verdict, principleIds) is { } rule)
            {
                return new ReviewVerdictCheck.Rejected(rule);
            }

            if (CriteriaRejection(verdict, facts) is { } criteria)
            {
                return new ReviewVerdictCheck.Rejected(criteria);
            }

            if (TestsRejection(verdict, facts) is { } tests)
            {
                return new ReviewVerdictCheck.Rejected(tests);
            }

            return new ReviewVerdictCheck.Accepted(new Valid(verdict, facts, DeriveOutcome(verdict, facts)));
        }

        // Code, never the model, decides the outcome (ADR 0113): anything that blocks requests changes, and a criterion
        // nobody could verify, or an issue with none, leaves the approval to a person.
        private static ReviewVerdictOutcome DeriveOutcome(ReviewVerdict verdict, SuppliedReviewFacts facts)
        {
            if (verdict.Findings.Any(finding => finding.Severity == ReviewSeverity.Blocking)
                || verdict.Tests.Assessment == ReviewTestsAssessment.Missing
                || verdict.Criteria.Any(answer => answer.Status == ReviewCriterionStatus.Unmet))
            {
                return ReviewVerdictOutcome.ChangesRequested;
            }

            if (facts.Criteria.Count == 0
                || verdict.Criteria.Any(answer => answer.Status == ReviewCriterionStatus.NotVerifiable))
            {
                return ReviewVerdictOutcome.ApprovedForPerson;
            }

            return ReviewVerdictOutcome.Approved;
        }
    }

    private static FailureReason.VerdictInvalid? DimensionRejection(ReviewVerdict verdict)
    {
        foreach (ReviewDimension dimension in verdict.Dimensions)
        {
            bool hasFinding = verdict.Findings.Any(finding => finding.Category == dimension.Category);
            bool hasBlocking = verdict.Findings.Any(finding =>
                finding.Category == dimension.Category && finding.Severity == ReviewSeverity.Blocking);

            if (dimension.Status == ReviewDimensionStatus.Findings && !hasFinding)
            {
                return Invalid($"Review verdict marks the '{TokenOf(dimension.Category)}' dimension as findings but lists no finding in it");
            }

            if (dimension.Status != ReviewDimensionStatus.Findings && hasBlocking)
            {
                return Invalid($"Review verdict marks the '{TokenOf(dimension.Category)}' dimension as {StatusToken(dimension.Status)} beside a blocking finding in it");
            }
        }

        return null;
    }

    private static FailureReason.VerdictInvalid? RuleRejection(ReviewVerdict verdict, IReadOnlySet<string> principleIds)
    {
        for (int i = 0; i < verdict.Findings.Count; i++)
        {
            ReviewFinding finding = verdict.Findings[i];
            ReviewRule? rule = finding.Rule;
            bool citesPrinciple = rule?.Principle is not null;
            bool citesConvention = rule is { File: not null } or { Quote: not null };
            string label = $"finding {i + 1} ({finding.Path})";

            if (finding.Severity == ReviewSeverity.Blocking && RuleBackedCategories.Contains(finding.Category))
            {
                if (!citesPrinciple && !citesConvention)
                {
                    return Invalid($"Review verdict's blocking {TokenOf(finding.Category)} {label} cites no rule");
                }

                if (citesPrinciple && citesConvention)
                {
                    return Invalid($"Review verdict's blocking {TokenOf(finding.Category)} {label} cites both a principle and a convention file");
                }
            }

            if (citesPrinciple && !principleIds.Contains(rule!.Principle!))
            {
                return Invalid($"Review verdict's {label} cites principle '{rule.Principle}', which is not a principle id of the role");
            }

            if (citesConvention && (string.IsNullOrWhiteSpace(rule!.File) || string.IsNullOrWhiteSpace(rule.Quote)))
            {
                return Invalid($"Review verdict's {label} cites a convention without both a file and a quote");
            }
        }

        return null;
    }

    private static FailureReason.VerdictInvalid? CriteriaRejection(ReviewVerdict verdict, SuppliedReviewFacts facts)
    {
        HashSet<string> supplied = [.. facts.Criteria.Select(criterion => criterion.Id)];
        HashSet<string> answered = new(StringComparer.Ordinal);

        foreach (ReviewCriterionAnswer answer in verdict.Criteria)
        {
            if (!answered.Add(answer.Id))
            {
                return Invalid($"Review verdict answers criterion {answer.Id} more than once");
            }

            if (!supplied.Contains(answer.Id))
            {
                return Invalid($"Review verdict answers criterion {answer.Id}, which was not supplied to the run");
            }
        }

        string[] missing = [.. facts.Criteria.Select(criterion => criterion.Id).Where(id => !answered.Contains(id))];

        return missing.Length > 0
            ? Invalid($"Review verdict does not answer criteria {string.Join(", ", missing)}")
            : null;
    }

    private static FailureReason.VerdictInvalid? TestsRejection(ReviewVerdict verdict, SuppliedReviewFacts facts)
    {
        if (!facts.ProductionChangedWithoutTests || verdict.Tests.Assessment != ReviewTestsAssessment.Adequate)
        {
            return null;
        }

        if (verdict.Tests.CoveringTests.Count == 0)
        {
            return Invalid("Review verdict judges the tests adequate for a change to production code with no test change, but names no covering test");
        }

        string? notATest = verdict.Tests.CoveringTests.FirstOrDefault(path =>
            string.IsNullOrEmpty(path) || ChangedFileClassifier.Classify(path) != ChangedFileClass.Test);

        return notATest is null
            ? null
            : Invalid($"Review verdict names '{notATest}' as a covering test, which is not a test path");
    }

    private static FailureReason.VerdictInvalid Invalid(string detail) => new(detail);

    private static string TokenOf(ReviewCategory category) =>
        CategoryTokens.First(pair => pair.Value == category).Key;

    private static string StatusToken(ReviewDimensionStatus status) =>
        DimensionStatusTokens.First(pair => pair.Value == status).Key;

    private static bool HasExactly(JsonObject node, string[] allowed, string[] required) =>
        node.All(property => allowed.Contains(property.Key, StringComparer.Ordinal))
        && required.All(node.ContainsKey);

    private static JsonObject? ReadObject(JsonNode? node) =>
        node is not null && node.GetValueKind() == JsonValueKind.Object ? node.AsObject() : null;

    private static JsonArray? ReadArray(JsonNode? node) =>
        node is not null && node.GetValueKind() == JsonValueKind.Array ? node.AsArray() : null;

    private static List<ReviewFinding>? ReadFindings(JsonNode? node)
    {
        if (ReadArray(node) is not { } array)
        {
            return null;
        }

        List<ReviewFinding> findings = [];

        foreach (JsonNode? entry in array)
        {
            if (ReadObject(entry) is not { } finding
                || !HasExactly(finding, FindingProperties, [PathProperty, SeverityProperty, CategoryProperty, BodyProperty])
                || VerdictQuestionsReader.ReadString(finding[PathProperty]) is not { } path
                || !TryReadLine(finding, out int? line)
                || !TryReadToken(finding[SeverityProperty], SeverityTokens, out ReviewSeverity severity)
                || ReadCategory(finding[CategoryProperty]) is not { } category
                || !TryReadRule(finding, out ReviewRule? rule)
                || VerdictQuestionsReader.ReadString(finding[BodyProperty]) is not { Length: > 0 } body)
            {
                return null;
            }

            findings.Add(new ReviewFinding(path, line, severity, category, rule, body));
        }

        return findings;
    }

    private static bool TryReadLine(JsonObject finding, out int? line)
    {
        line = null;

        if (!finding.ContainsKey(LineProperty))
        {
            return true;
        }

        if (finding[LineProperty] is not JsonValue value
            || value.GetValueKind() != JsonValueKind.Number
            || !value.TryGetValue(out int number)
            || number < 1)
        {
            return false;
        }

        line = number;
        return true;
    }

    private static bool TryReadRule(JsonObject finding, out ReviewRule? rule)
    {
        rule = null;

        if (!finding.ContainsKey(RuleProperty))
        {
            return true;
        }

        if (ReadObject(finding[RuleProperty]) is not { } node || !HasExactly(node, RuleProperties, []))
        {
            return false;
        }

        string? principle = null;
        string? file = null;
        string? quote = null;

        if ((node.ContainsKey(PrincipleProperty) && (principle = VerdictQuestionsReader.ReadString(node[PrincipleProperty])) is null)
            || (node.ContainsKey(FileProperty) && (file = VerdictQuestionsReader.ReadString(node[FileProperty])) is null)
            || (node.ContainsKey(QuoteProperty) && (quote = VerdictQuestionsReader.ReadString(node[QuoteProperty])) is null))
        {
            return false;
        }

        rule = new ReviewRule(principle, file, quote);
        return true;
    }

    private static List<ReviewDimension>? ReadDimensions(JsonNode? node)
    {
        string[] keys = [.. CategoryTokens.Select(pair => pair.Key)];

        if (ReadObject(node) is not { } dimensions || !HasExactly(dimensions, keys, keys))
        {
            return null;
        }

        List<ReviewDimension> read = [];

        foreach (KeyValuePair<string, ReviewCategory> category in CategoryTokens)
        {
            if (ReadObject(dimensions[category.Key]) is not { } dimension
                || !HasExactly(dimension, DimensionProperties, DimensionProperties)
                || !TryReadToken(dimension[StatusProperty], DimensionStatusTokens, out ReviewDimensionStatus status)
                || VerdictQuestionsReader.ReadString(dimension[NoteProperty]) is not { } note)
            {
                return null;
            }

            read.Add(new ReviewDimension(category.Value, status, note));
        }

        return read;
    }

    private static List<ReviewCriterionAnswer>? ReadCriteria(JsonNode? node)
    {
        if (ReadArray(node) is not { } array)
        {
            return null;
        }

        List<ReviewCriterionAnswer> answers = [];

        foreach (JsonNode? entry in array)
        {
            if (ReadObject(entry) is not { } answer
                || !HasExactly(answer, CriterionProperties, CriterionProperties)
                || VerdictQuestionsReader.ReadString(answer[IdProperty]) is not { } id
                || !CriterionId().IsMatch(id)
                || !TryReadToken(answer[StatusProperty], CriterionStatusTokens, out ReviewCriterionStatus status)
                || VerdictQuestionsReader.ReadString(answer[EvidenceProperty]) is not { } evidence)
            {
                return null;
            }

            answers.Add(new ReviewCriterionAnswer(id, status, evidence));
        }

        return answers;
    }

    private static ReviewTests? ReadTests(JsonNode? node)
    {
        if (ReadObject(node) is not { } tests
            || !HasExactly(tests, TestsProperties, TestsProperties)
            || !TryReadToken(tests[AssessmentProperty], AssessmentTokens, out ReviewTestsAssessment assessment)
            || VerdictQuestionsReader.ReadString(tests[ReasonProperty]) is not { } reason
            || ReadArray(tests[CoveringTestsProperty]) is not { } covering)
        {
            return null;
        }

        List<string> paths = [];

        foreach (JsonNode? entry in covering)
        {
            if (VerdictQuestionsReader.ReadString(entry) is not { } path)
            {
                return null;
            }

            paths.Add(path);
        }

        return new ReviewTests(assessment, reason, paths);
    }

    private static ReviewCategory? ReadCategory(JsonNode? node)
    {
        string? token = VerdictQuestionsReader.ReadString(node);

        foreach (KeyValuePair<string, ReviewCategory> pair in CategoryTokens)
        {
            if (string.Equals(pair.Key, token, StringComparison.Ordinal))
            {
                return pair.Value;
            }
        }

        return null;
    }

    private static bool TryReadToken<T>(JsonNode? node, IReadOnlyDictionary<string, T> tokens, out T value)
        where T : struct
    {
        if (VerdictQuestionsReader.ReadString(node) is { } token && tokens.TryGetValue(token, out value))
        {
            return true;
        }

        value = default;
        return false;
    }

    // Anchored by `$` in .NET, which also matches before a final newline; `\z` keeps "AC1\n" out.
    [GeneratedRegex(@"^AC[0-9]+\z", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex CriterionId();
}

/// <summary>What checking a review verdict against its run's facts decided.</summary>
internal abstract record ReviewVerdictCheck
{
    private ReviewVerdictCheck()
    {
    }

    /// <summary>The verdict passed every check; its outcome is derived.</summary>
    internal sealed record Accepted(ReviewVerdict.Valid Verdict) : ReviewVerdictCheck;

    /// <summary>The verdict broke a rule and the run fails as <c>verdict_invalid</c>.</summary>
    internal sealed record Rejected(FailureReason.VerdictInvalid Reason) : ReviewVerdictCheck;
}

/// <summary>
/// Records a review verdict that matches the schema's shape as <see cref="RunVerdictStatus.Done"/>. Its checks against
/// the run's facts and its outcome come from <see cref="ReviewVerdict.CheckAgainst"/> at resolution (ADR 0113).
/// </summary>
internal sealed class ReviewVerdictParser : IWorkerVerdictParser
{
    public WorkerRole Role => WorkerRole.Review;

    public RunVerdict Parse(JsonObject structuredOutput) =>
        ReviewVerdict.TryParse(structuredOutput) is null ? RunVerdict.Invalid() : RunVerdict.Done();
}
