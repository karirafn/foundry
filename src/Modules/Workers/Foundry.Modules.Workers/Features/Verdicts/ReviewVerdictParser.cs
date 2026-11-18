using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.ValueObjects;

namespace Foundry.Modules.Workers.Features.Verdicts;

/// <summary>
/// Parses the review role's <c>structured_output</c> into a <see cref="ReviewVerdict"/>, mirroring
/// <c>workers/roles/review/verdict.schema.json</c> (ADR 0113): any property, type, token or length the schema forbids
/// breaks the verdict. A parsed verdict still has to pass its checks against the run's facts.
/// </summary>
internal sealed partial class ReviewVerdictParser : IWorkerVerdictParser
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

    internal const int SummaryMaxLength = 2000;
    internal const int BodyMaxLength = 2000;
    internal const int NoteMaxLength = 500;
    internal const int EvidenceMaxLength = 1000;
    internal const int ReasonMaxLength = 1000;
    internal const int MaxItems = 50;

    internal static readonly string[] TopLevelProperties =
        [SummaryProperty, FindingsProperty, DimensionsProperty, CriteriaProperty, TestsProperty];

    internal static readonly string[] FindingProperties =
        [PathProperty, LineProperty, SeverityProperty, CategoryProperty, RuleProperty, BodyProperty];

    internal static readonly string[] RequiredFindingProperties = [PathProperty, SeverityProperty, CategoryProperty, BodyProperty];

    internal static readonly string[] RuleProperties = [PrincipleProperty, FileProperty, QuoteProperty];

    internal static readonly string[] DimensionProperties = [StatusProperty, NoteProperty];

    internal static readonly string[] CriterionProperties = [IdProperty, StatusProperty, EvidenceProperty];

    internal static readonly string[] TestsProperties = [AssessmentProperty, ReasonProperty, CoveringTestsProperty];

    public WorkerRole Role => WorkerRole.Review;

    /// <summary>
    /// Records a schema-valid review verdict as <see cref="RunVerdictStatus.Done"/>; the run's outcome comes from the
    /// checked verdict itself (ADR 0113).
    /// </summary>
    public RunVerdict Parse(JsonObject structuredOutput) =>
        TryParse(structuredOutput) is null ? RunVerdict.Invalid() : RunVerdict.Done();

    /// <summary>Returns the verdict, or <see langword="null"/> when <paramref name="output"/> breaks the schema.</summary>
    public static ReviewVerdict? TryParse(JsonObject output)
    {
        ArgumentNullException.ThrowIfNull(output);

        if (!HasExactly(output, TopLevelProperties, TopLevelProperties)
            || ReadText(output[SummaryProperty], 1, SummaryMaxLength) is not { } summary
            || ReadArray(output[FindingsProperty], ReadFinding) is not { } findings
            || ReadDimensions(output[DimensionsProperty]) is not { } dimensions
            || ReadArray(output[CriteriaProperty], ReadCriterion) is not { } criteria
            || ReadTests(output[TestsProperty]) is not { } tests)
        {
            return null;
        }

        return new ReviewVerdict(summary, findings, dimensions, criteria, tests);
    }

    /// <summary>Writes <paramref name="verdict"/> in the schema's shape, so <see cref="TryParse"/> reads it back unchanged.</summary>
    public static JsonObject ToJson(ReviewVerdict verdict)
    {
        ArgumentNullException.ThrowIfNull(verdict);

        JsonObject dimensions = [];

        foreach (ReviewDimension dimension in verdict.Dimensions)
        {
            dimensions[ReviewTokens.Category(dimension.Category)] = new JsonObject
            {
                [StatusProperty] = ReviewTokens.DimensionStatus(dimension.Status),
                [NoteProperty] = dimension.Note,
            };
        }

        return new JsonObject
        {
            [SummaryProperty] = verdict.Summary,
            [FindingsProperty] = new JsonArray([.. verdict.Findings.Select(FindingToJson)]),
            [DimensionsProperty] = dimensions,
            [CriteriaProperty] = new JsonArray([.. verdict.Criteria.Select(criterion => (JsonNode)new JsonObject
            {
                [IdProperty] = criterion.Id,
                [StatusProperty] = ReviewTokens.CriterionStatus(criterion.Status),
                [EvidenceProperty] = criterion.Evidence,
            })]),
            [TestsProperty] = new JsonObject
            {
                [AssessmentProperty] = ReviewTokens.TestsAssessment(verdict.Tests.Assessment),
                [ReasonProperty] = verdict.Tests.Reason,
                [CoveringTestsProperty] = new JsonArray([.. verdict.Tests.CoveringTests.Select(path => (JsonNode)JsonValue.Create(path))]),
            },
        };
    }

    private static JsonNode FindingToJson(ReviewFinding finding)
    {
        JsonObject node = new()
        {
            [PathProperty] = finding.Path,
            [SeverityProperty] = ReviewTokens.Severity(finding.Severity),
            [CategoryProperty] = ReviewTokens.Category(finding.Category),
            [BodyProperty] = finding.Body,
        };

        if (finding.Line is { } line)
        {
            node[LineProperty] = line;
        }

        if (finding.Rule is { } rule)
        {
            JsonObject ruleNode = [];

            if (rule.Principle is not null)
            {
                ruleNode[PrincipleProperty] = rule.Principle;
            }

            if (rule.File is not null)
            {
                ruleNode[FileProperty] = rule.File;
            }

            if (rule.Quote is not null)
            {
                ruleNode[QuoteProperty] = rule.Quote;
            }

            node[RuleProperty] = ruleNode;
        }

        return node;
    }

    private static ReviewFinding? ReadFinding(JsonNode? node)
    {
        if (node is not JsonObject finding
            || !HasExactly(finding, RequiredFindingProperties, FindingProperties)
            || ReadText(finding[PathProperty], 0, int.MaxValue) is not { } path
            || ReadToken(finding[SeverityProperty], ReviewTokens.Severities) is not { } severity
            || ReadToken(finding[CategoryProperty], ReviewTokens.Categories) is not { } category
            || ReadText(finding[BodyProperty], 1, BodyMaxLength) is not { } body)
        {
            return null;
        }

        int? line = null;

        if (finding.ContainsKey(LineProperty))
        {
            if (finding[LineProperty] is not JsonValue lineValue
                || lineValue.GetValueKind() != JsonValueKind.Number
                || !lineValue.TryGetValue(out int readLine)
                || readLine < 1)
            {
                return null;
            }

            line = readLine;
        }

        ReviewRule? rule = null;

        if (finding.ContainsKey(RuleProperty))
        {
            if (finding[RuleProperty] is not JsonObject ruleObject
                || !HasExactly(ruleObject, [], RuleProperties)
                || !TryReadOptionalString(ruleObject, PrincipleProperty, out string? principle)
                || !TryReadOptionalString(ruleObject, FileProperty, out string? file)
                || !TryReadOptionalString(ruleObject, QuoteProperty, out string? quote))
            {
                return null;
            }

            rule = new ReviewRule(principle, file, quote);
        }

        return new ReviewFinding(path, line, severity, category, rule, body);
    }

    private static List<ReviewDimension>? ReadDimensions(JsonNode? node)
    {
        string[] categories = [.. ReviewTokens.Categories.Keys];

        if (node is not JsonObject dimensions || !HasExactly(dimensions, categories, categories))
        {
            return null;
        }

        List<ReviewDimension> read = [];

        foreach ((string token, ReviewCategory category) in ReviewTokens.Categories)
        {
            if (dimensions[token] is not JsonObject dimension
                || !HasExactly(dimension, DimensionProperties, DimensionProperties)
                || ReadToken(dimension[StatusProperty], ReviewTokens.DimensionStatuses) is not { } status
                || ReadText(dimension[NoteProperty], 0, NoteMaxLength) is not { } note)
            {
                return null;
            }

            read.Add(new ReviewDimension(category, status, note));
        }

        return read;
    }

    private static ReviewCriterionAnswer? ReadCriterion(JsonNode? node)
    {
        if (node is not JsonObject criterion
            || !HasExactly(criterion, CriterionProperties, CriterionProperties)
            || ReadText(criterion[IdProperty], 0, int.MaxValue) is not { } id
            || !CriterionIdPattern().IsMatch(id)
            || ReadToken(criterion[StatusProperty], ReviewTokens.CriterionStatuses) is not { } status
            || ReadText(criterion[EvidenceProperty], 0, EvidenceMaxLength) is not { } evidence)
        {
            return null;
        }

        return new ReviewCriterionAnswer(id, status, evidence);
    }

    private static ReviewTests? ReadTests(JsonNode? node)
    {
        if (node is not JsonObject tests
            || !HasExactly(tests, TestsProperties, TestsProperties)
            || ReadToken(tests[AssessmentProperty], ReviewTokens.TestsAssessments) is not { } assessment
            || ReadText(tests[ReasonProperty], 0, ReasonMaxLength) is not { } reason
            || ReadArray(tests[CoveringTestsProperty], item => ReadText(item, 0, int.MaxValue)) is not { } coveringTests)
        {
            return null;
        }

        return new ReviewTests(assessment, reason, coveringTests);
    }

    // Every required property is present and no property is unknown (the schema's additionalProperties: false).
    private static bool HasExactly(JsonObject node, IReadOnlyCollection<string> required, IReadOnlyCollection<string> allowed) =>
        required.All(node.ContainsKey) && node.All(property => allowed.Contains(property.Key));

    private static List<T>? ReadArray<T>(JsonNode? node, Func<JsonNode?, T?> readItem)
        where T : class
    {
        if (node is not JsonArray array || array.Count > MaxItems)
        {
            return null;
        }

        List<T> items = [];

        foreach (JsonNode? item in array)
        {
            if (readItem(item) is not { } read)
            {
                return null;
            }

            items.Add(read);
        }

        return items;
    }

    // Lengths count code points, as the schema's minLength and maxLength do.
    private static string? ReadText(JsonNode? node, int minLength, int maxLength)
    {
        if (VerdictQuestionsReader.ReadString(node) is not { } text)
        {
            return null;
        }

        int length = text.EnumerateRunes().Count();
        return length >= minLength && length <= maxLength ? text : null;
    }

    private static TEnum? ReadToken<TEnum>(JsonNode? node, IReadOnlyDictionary<string, TEnum> tokens)
        where TEnum : struct, Enum =>
        VerdictQuestionsReader.ReadString(node) is { } token && tokens.TryGetValue(token, out TEnum value) ? value : null;

    private static bool TryReadOptionalString(JsonObject node, string property, out string? value)
    {
        value = null;

        if (!node.ContainsKey(property))
        {
            return true;
        }

        value = VerdictQuestionsReader.ReadString(node[property]);
        return value is not null;
    }

    [GeneratedRegex("^AC[0-9]+$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex CriterionIdPattern();
}
