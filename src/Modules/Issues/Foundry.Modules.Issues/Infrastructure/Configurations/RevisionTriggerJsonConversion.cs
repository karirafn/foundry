using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;

using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Foundry.Modules.Issues.Infrastructure.Configurations;

internal static class RevisionTriggerJsonConversion
{
    public static ValueConverter<RevisionTrigger, string> Converter { get; } = new(
        trigger => Serialize(trigger),
        json => Deserialize(json));

    public static ValueComparer<RevisionTrigger> Comparer { get; } = new(
        (a, b) => a != null && b != null && a.Discriminator == b.Discriminator && TriggersAreEqual(a, b),
        trigger => trigger.GetHashCode(),
        trigger => trigger);

    private static string Serialize(RevisionTrigger trigger)
    {
        RevisionTriggerDto dto = trigger switch
        {
            RevisionTrigger.ReviewFeedback rf => new RevisionTriggerDto(
                trigger.Discriminator,
                [.. rf.Comments.Select(c => new ReviewCommentDto(c.Body, c.FilePath, c.Line))],
                rf.OmittedCommentCount,
                rf.NewestConsumedCommentAt,
                null),
            RevisionTrigger.ReviewFindings findings => new RevisionTriggerDto(trigger.Discriminator, null, null, null, null)
            {
                Findings = [.. findings.Findings.Select(ReviewFindingDto.From)],
                UnmetCriteria = [.. findings.UnmetCriteria.Select(c => new UnmetCriterionDto(c.Id, c.Text, c.Evidence))],
                TestsAssessment = findings.TestsAssessment,
                TestsReason = findings.TestsReason,
            },
            RevisionTrigger.FailedChecks fc => new RevisionTriggerDto(
                trigger.Discriminator,
                null,
                null,
                null,
                [.. fc.FailingChecks.Select(c => new FailingCheckDto(c.Name, c.Conclusion, c.DetailsUrl, c.IsInfrastructureFailure))]),
            RevisionTrigger.RebaseConflict => new RevisionTriggerDto(trigger.Discriminator, null, null, null, null),
            RevisionTrigger.InputProvided => new RevisionTriggerDto(trigger.Discriminator, null, null, null, null),
            _ => throw new UnreachableException($"Unknown RevisionTrigger discriminator: '{trigger.Discriminator}'"),
        };

        return JsonSerializer.Serialize(dto, (JsonSerializerOptions?)null);
    }

    private static RevisionTrigger Deserialize(string? json)
    {
        if (json is null)
        {
            return new RevisionTrigger.ReviewFeedback([], 0, null);
        }

        RevisionTriggerDto dto = JsonSerializer.Deserialize<RevisionTriggerDto>(json, (JsonSerializerOptions?)null)
            ?? new RevisionTriggerDto("review_feedback", [], 0, null, null);

        return dto.Discriminator switch
        {
            "review_feedback" => new RevisionTrigger.ReviewFeedback(
                [.. (dto.Comments ?? []).Select(c => new ReviewComment(c.Body, c.FilePath, c.Line))],
                dto.OmittedCommentCount ?? 0,
                dto.NewestConsumedCommentAt),
            "review_findings" => new RevisionTrigger.ReviewFindings(
                [.. (dto.Findings ?? []).Select(f => f.ToFinding())],
                [.. (dto.UnmetCriteria ?? []).Select(c => new ReviewUnmetCriterion(c.Id, c.Text, c.Evidence))],
                dto.TestsAssessment ?? throw new UnreachableException("A review_findings trigger was stored without its tests assessment."),
                dto.TestsReason ?? string.Empty),
            "failed_checks" => new RevisionTrigger.FailedChecks(
                [.. (dto.FailingChecks ?? []).Select(c => new FailingCheck(c.Name, c.Conclusion, c.DetailsUrl, c.IsInfrastructureFailure))]),
            "rebase_conflict" => new RevisionTrigger.RebaseConflict(),
            "input_provided" => new RevisionTrigger.InputProvided(),
            _ => throw new UnreachableException($"Unknown RevisionTrigger discriminator: '{dto.Discriminator}'"),
        };
    }

    private static bool TriggersAreEqual(RevisionTrigger a, RevisionTrigger b) =>
        (a, b) switch
        {
            (RevisionTrigger.ReviewFeedback rfA, RevisionTrigger.ReviewFeedback rfB) =>
                rfA.OmittedCommentCount == rfB.OmittedCommentCount
                && rfA.NewestConsumedCommentAt == rfB.NewestConsumedCommentAt
                && rfA.Comments.SequenceEqual(rfB.Comments),
            (RevisionTrigger.ReviewFindings findingsA, RevisionTrigger.ReviewFindings findingsB) =>
                findingsA.TestsAssessment == findingsB.TestsAssessment
                && findingsA.TestsReason == findingsB.TestsReason
                && findingsA.Findings.SequenceEqual(findingsB.Findings)
                && findingsA.UnmetCriteria.SequenceEqual(findingsB.UnmetCriteria),
            (RevisionTrigger.FailedChecks fcA, RevisionTrigger.FailedChecks fcB) =>
                fcA.FailingChecks.SequenceEqual(fcB.FailingChecks),
            (RevisionTrigger.RebaseConflict, RevisionTrigger.RebaseConflict) => true,
            (RevisionTrigger.InputProvided, RevisionTrigger.InputProvided) => true,
            _ => false,
        };

    private sealed record RevisionTriggerDto(
        [property: JsonPropertyName("discriminator")] string Discriminator,
        [property: JsonPropertyName("comments")] IReadOnlyList<ReviewCommentDto>? Comments,
        [property: JsonPropertyName("omittedCommentCount")] int? OmittedCommentCount,
        [property: JsonPropertyName("newestConsumedCommentAt")] DateTimeOffset? NewestConsumedCommentAt,
        [property: JsonPropertyName("failingChecks")] IReadOnlyList<FailingCheckDto>? FailingChecks)
    {
        // Written only for review_findings, so the rows of every other trigger keep their shape.
        [JsonPropertyName("findings")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public IReadOnlyList<ReviewFindingDto>? Findings { get; init; }

        [JsonPropertyName("unmetCriteria")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public IReadOnlyList<UnmetCriterionDto>? UnmetCriteria { get; init; }

        [JsonPropertyName("testsAssessment")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        [JsonConverter(typeof(JsonStringEnumConverter<ReviewTestsAssessment>))]
        public ReviewTestsAssessment? TestsAssessment { get; init; }

        [JsonPropertyName("testsReason")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? TestsReason { get; init; }
    }

    private sealed record ReviewCommentDto(
        [property: JsonPropertyName("body")] string Body,
        [property: JsonPropertyName("filePath")] string? FilePath,
        [property: JsonPropertyName("line")] int? Line);

    private sealed record ReviewFindingDto(
        [property: JsonPropertyName("path")] string Path,
        [property: JsonPropertyName("line")] int? Line,
        [property: JsonPropertyName("severity"), JsonConverter(typeof(JsonStringEnumConverter<ReviewSeverity>))] ReviewSeverity Severity,
        [property: JsonPropertyName("category"), JsonConverter(typeof(JsonStringEnumConverter<ReviewCategory>))] ReviewCategory Category,
        [property: JsonPropertyName("principle")] string? Principle,
        [property: JsonPropertyName("ruleFile")] string? RuleFile,
        [property: JsonPropertyName("quote")] string? Quote,
        [property: JsonPropertyName("hasRule")] bool HasRule,
        [property: JsonPropertyName("body")] string Body)
    {
        public static ReviewFindingDto From(ReviewFinding finding) =>
            new(
                finding.Path,
                finding.Line,
                finding.Severity,
                finding.Category,
                finding.Rule?.Principle,
                finding.Rule?.File,
                finding.Rule?.Quote,
                finding.Rule is not null,
                finding.Body);

        public ReviewFinding ToFinding() =>
            new(Path, Line, Severity, Category, HasRule ? new ReviewRule(Principle, RuleFile, Quote) : null, Body);
    }

    private sealed record UnmetCriterionDto(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("text")] string Text,
        [property: JsonPropertyName("evidence")] string Evidence);

    private sealed record FailingCheckDto(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("conclusion")] string Conclusion,
        [property: JsonPropertyName("detailsUrl")] string? DetailsUrl,
        // Rows written before issue #968 carry no flag and read as fixable checks.
        [property: JsonPropertyName("infrastructure")] bool IsInfrastructureFailure = false);
}
