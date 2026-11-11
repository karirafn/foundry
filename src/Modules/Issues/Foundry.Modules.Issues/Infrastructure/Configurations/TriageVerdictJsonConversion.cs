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

    public static ValueConverter<TriageVerdict, string> Converter { get; } = new(
        verdict => Serialize(verdict),
        json => Deserialize(json));

    public static ValueComparer<TriageVerdict> Comparer { get; } = new(
        (a, b) => a == b,
        verdict => verdict.GetHashCode(),
        verdict => verdict);

    private static string Serialize(TriageVerdict verdict)
    {
        TriageVerdictDto dto = verdict switch
        {
            TriageVerdict.Ready ready => new TriageVerdictDto(ReadyOutcome, ready.Kind.Value, null, ready.DecidedAt),
            TriageVerdict.NeedsInput needsInput => new TriageVerdictDto(
                NeedsInputOutcome,
                needsInput.Kind.Value,
                [.. needsInput.Questions.Select(q => new QuestionDto(q.Question, q.Context))],
                needsInput.DecidedAt),
            _ => throw new UnreachableException($"Unknown TriageVerdict variant: {verdict.GetType().Name}"),
        };

        return JsonSerializer.Serialize(dto, (JsonSerializerOptions?)null);
    }

    private static TriageVerdict Deserialize(string json)
    {
        TriageVerdictDto dto = JsonSerializer.Deserialize<TriageVerdictDto>(json, (JsonSerializerOptions?)null)
            ?? throw new UnreachableException("Stored triage_verdict JSON deserialised to null.");

        IssueKind kind = IssueKind.FromLabel(dto.Kind)
            ?? throw new UnreachableException($"Stored triage verdict kind '{dto.Kind}' is not a known issue kind.");

        return dto.Outcome switch
        {
            ReadyOutcome => new TriageVerdict.Ready(kind, dto.DecidedAt),
            NeedsInputOutcome => new TriageVerdict.NeedsInput(
                kind,
                [.. (dto.Questions ?? []).Select(q => new InputQuestion(q.Question, q.Context))],
                dto.DecidedAt),
            _ => throw new UnreachableException($"Unknown triage verdict outcome: '{dto.Outcome}'"),
        };
    }

    private sealed record TriageVerdictDto(
        [property: JsonPropertyName("outcome")] string Outcome,
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("questions")] IReadOnlyList<QuestionDto>? Questions,
        [property: JsonPropertyName("decidedAt")] DateTimeOffset DecidedAt);

    private sealed record QuestionDto(
        [property: JsonPropertyName("question")] string Question,
        [property: JsonPropertyName("context")] string Context);
}
