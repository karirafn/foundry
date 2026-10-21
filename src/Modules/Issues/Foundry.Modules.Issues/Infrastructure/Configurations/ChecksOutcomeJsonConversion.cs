using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Shared;

using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Foundry.Modules.Issues.Infrastructure.Configurations;

internal static class ChecksOutcomeJsonConversion
{
    public static ValueConverter<ChecksOutcome, string> Converter { get; } = new(
        outcome => Serialize(outcome),
        json => Deserialize(json));

    public static ValueComparer<ChecksOutcome> Comparer { get; } = new(
        (a, b) => a != null && b != null && a.Discriminator == b.Discriminator
            && OutcomesAreEqual(a, b),
        outcome => outcome.GetHashCode(),
        outcome => outcome);

    private static string Serialize(ChecksOutcome outcome)
    {
        ChecksOutcomeDto dto = outcome switch
        {
            ChecksOutcome.StillFailing sf => new ChecksOutcomeDto(
                outcome.Discriminator,
                [.. sf.FailingChecks.Select(c => new FailingCheckDto(c.Name, c.Conclusion, c.DetailsUrl))]),
            _ => new ChecksOutcomeDto(outcome.Discriminator, []),
        };

        return JsonSerializer.Serialize(dto, (JsonSerializerOptions?)null);
    }

    private static ChecksOutcome Deserialize(string? json)
    {
        if (json is null)
        {
            return new ChecksOutcome.Passed();
        }

        ChecksOutcomeDto dto = JsonSerializer.Deserialize<ChecksOutcomeDto>(json, (JsonSerializerOptions?)null)
            ?? new ChecksOutcomeDto("passed", []);

        return dto.Discriminator switch
        {
            "passed" => new ChecksOutcome.Passed(),
            "none_detected" => new ChecksOutcome.NoneDetected(),
            "unfinished" => new ChecksOutcome.Unfinished(),
            "still_failing" => new ChecksOutcome.StillFailing(
                [.. (dto.FailingChecks ?? []).Select(c => new FailingCheck(c.Name, c.Conclusion, c.DetailsUrl))]),
            "unreadable" => new ChecksOutcome.Unreadable(),
            _ => throw new UnreachableException($"Unknown ChecksOutcome discriminator: '{dto.Discriminator}'"),
        };
    }

    private static bool OutcomesAreEqual(ChecksOutcome a, ChecksOutcome b)
    {
        if (a is ChecksOutcome.StillFailing sfA && b is ChecksOutcome.StillFailing sfB)
        {
            return sfA.FailingChecks.SequenceEqual(sfB.FailingChecks);
        }

        return a.Discriminator == b.Discriminator;
    }

    private sealed record ChecksOutcomeDto(
        [property: JsonPropertyName("discriminator")] string Discriminator,
        [property: JsonPropertyName("failingChecks")] IReadOnlyList<FailingCheckDto>? FailingChecks);

    private sealed record FailingCheckDto(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("conclusion")] string Conclusion,
        [property: JsonPropertyName("detailsUrl")] string? DetailsUrl);
}
