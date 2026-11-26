using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
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
    private const string RoutineDemand = "routine";
    private const string DemandingDemand = "demanding";

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
            TriageVerdict.Ready ready => new TriageVerdictDto(
                ReadyOutcome,
                ready.Kind.Value,
                null,
                null,
                ready.DecidedAt,
                JsonValue.Create(DemandToken(ready.Demand)),
                ready.DemandReason),
            TriageVerdict.NeedsInput needsInput => new TriageVerdictDto(
                NeedsInputOutcome,
                needsInput.Kind.Value,
                InputQuestionJson.ToNode(needsInput.Questions),
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
            ReadyOutcome => new TriageVerdict.Ready(kind, dto.DecidedAt, DemandOf(dto.Demand), dto.DemandReason),
            NeedsInputOutcome => new TriageVerdict.NeedsInput(
                kind,
                InputQuestionJson.FromNode(dto.Questions),
                dto.DecidedAt),
            BlockedOutcome => new TriageVerdict.Blocked(
                kind,
                [.. (dto.BlockedBy ?? []).Select(b => new TriageBlocker(b.Issue, b.Reason))],
                dto.DecidedAt),
            _ => null,
        };
    }

    private static string DemandToken(ImplementDemand demand) => demand switch
    {
        ImplementDemand.Routine => RoutineDemand,
        ImplementDemand.Demanding => DemandingDemand,
        _ => throw new UnreachableException($"Unknown ImplementDemand: {demand}"),
    };

    // A verdict stored before triage judged a demand, or holding a token this code does not know, reads as demanding:
    // the demand only selects a model, so it never makes the verdict itself unreadable.
    private static ImplementDemand DemandOf(JsonNode? stored) =>
        stored is JsonValue value && value.TryGetValue(out string? token) && token == RoutineDemand
            ? ImplementDemand.Routine
            : ImplementDemand.Demanding;

    private sealed record TriageVerdictDto(
        [property: JsonPropertyName("outcome")] string Outcome,
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("questions"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        JsonNode? Questions,
        [property: JsonPropertyName("blockedBy"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        IReadOnlyList<BlockedByDto>? BlockedBy,
        [property: JsonPropertyName("decidedAt")] DateTimeOffset DecidedAt,
        [property: JsonPropertyName("demand"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        JsonNode? Demand = null,
        [property: JsonPropertyName("demandReason"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? DemandReason = null);

    private sealed record BlockedByDto(
        [property: JsonPropertyName("issue")] int Issue,
        [property: JsonPropertyName("reason")] string Reason);
}
