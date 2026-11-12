using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Workers.Contracts;

using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Foundry.Modules.Issues.Infrastructure.Configurations;

/// <summary>JSON column mapping for an issue's latest <see cref="TriageVerdict"/> (ADR 0097).</summary>
internal static class TriageVerdictJsonConversion
{
    private const string ReadyOutcome = "ready";
    private const string NeedsInputOutcome = "needs_input";
    private const string BlockedOutcome = "blocked";

    public static ValueConverter<TriageVerdict?, string> Converter { get; } = new(
        verdict => Serialize(verdict!),
        json => Deserialize(json));

    public static ValueComparer<TriageVerdict> Comparer { get; } = new(
        (a, b) => a == b,
        verdict => verdict.GetHashCode(),
        verdict => verdict);

    private static string Serialize(TriageVerdict verdict)
    {
        TriageVerdictDto dto = verdict switch
        {
            TriageVerdict.Ready ready => new TriageVerdictDto(ReadyOutcome, ready.Kind.Value, null, null, ready.DecidedAt),
            TriageVerdict.NeedsInput needsInput => new TriageVerdictDto(
                NeedsInputOutcome,
                needsInput.Kind.Value,
                [.. needsInput.Questions.Select(q => new QuestionDto(q.Question, q.Context, q.Recommendation))],
                null,
                needsInput.DecidedAt),
            TriageVerdict.Blocked blocked => new TriageVerdictDto(
                BlockedOutcome,
                blocked.Kind.Value,
                null,
                [.. blocked.BlockedBy.Select(b => new BlockedByDto(b.IssueNumber, b.Reason))],
                blocked.DecidedAt),
            _ => throw new UnreachableException($"Unknown TriageVerdict variant: {verdict.GetType().Name}"),
        };

        return JsonSerializer.Serialize(dto, (JsonSerializerOptions?)null);
    }

    // A row that cannot form a verdict reads as no verdict: this converter runs while a repository's issues
    // materialise together, so a throw here would make every one of them unreadable, and an EF value converter has no
    // logger to report through. The stored text stays in the column until the issue's next state change, which copies
    // the null verdict into a new row and so drops the unreadable text. A SQL filter on TriageVerdict == null does not
    // match such a row, because the column itself is not NULL.
    private static TriageVerdict? Deserialize(string json)
    {
        try
        {
            return Parse(json);
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        {
            return null;
        }
    }

    private static TriageVerdict? Parse(string json)
    {
        TriageVerdictDto? dto = JsonSerializer.Deserialize<TriageVerdictDto>(json, (JsonSerializerOptions?)null);

        if (dto is null || IssueKind.FromLabel(dto.Kind) is not IssueKind kind)
        {
            return null;
        }

        return dto.Outcome switch
        {
            ReadyOutcome => new TriageVerdict.Ready(kind, dto.DecidedAt),
            NeedsInputOutcome => new TriageVerdict.NeedsInput(
                kind,
                [.. (dto.Questions ?? []).Select(q => new InputQuestion(q.Question, q.Context, q.Recommendation))],
                dto.DecidedAt),
            BlockedOutcome => new TriageVerdict.Blocked(
                kind,
                [.. (dto.BlockedBy ?? []).Select(b => new TriageBlocker(b.Issue, b.Reason))],
                dto.DecidedAt),
            _ => null,
        };
    }

    private sealed record TriageVerdictDto(
        [property: JsonPropertyName("outcome")] string Outcome,
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("questions"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        IReadOnlyList<QuestionDto>? Questions,
        [property: JsonPropertyName("blockedBy"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        IReadOnlyList<BlockedByDto>? BlockedBy,
        [property: JsonPropertyName("decidedAt")] DateTimeOffset DecidedAt);

    private sealed record BlockedByDto(
        [property: JsonPropertyName("issue")] int Issue,
        [property: JsonPropertyName("reason")] string Reason);

    private sealed record QuestionDto(
        [property: JsonPropertyName("question")] string Question,
        [property: JsonPropertyName("context")] string Context,
        [property: JsonPropertyName("recommendation")] string? Recommendation);
}
