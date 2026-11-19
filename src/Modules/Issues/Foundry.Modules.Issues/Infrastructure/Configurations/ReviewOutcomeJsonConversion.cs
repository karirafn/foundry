using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

using Foundry.Modules.Issues.Domain.ValueObjects;

using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Foundry.Modules.Issues.Infrastructure.Configurations;

/// <summary>
/// Maps <see cref="ReviewOutcome"/> to the flat JSON <c>{"discriminator":"skipped","reason":"verdict_invalid"}</c>
/// stored in the <c>review_outcome</c> column. A <c>NULL</c> column (review issues whose review step did not fail open)
/// materialises as a <see langword="null"/> outcome.
/// </summary>
internal static class ReviewOutcomeJsonConversion
{
    // EF Core never passes NULL through a converter on a nullable column, so the null-forgiving operators
    // only satisfy the nullable signature the property builder expects.
    public static ValueConverter<ReviewOutcome?, string> Converter { get; } = new(
        outcome => Serialize(outcome!),
        json => Deserialize(json));

    public static ValueComparer<ReviewOutcome?> Comparer { get; } = new(
        (a, b) => a == b,
        outcome => outcome == null ? 0 : outcome.GetHashCode(),
        outcome => outcome);

    private static string Serialize(ReviewOutcome outcome)
    {
        ReviewOutcomeDto dto = new(
            outcome.Discriminator,
            outcome is ReviewOutcome.Skipped skipped ? skipped.Reason : null);

        return JsonSerializer.Serialize(dto, (JsonSerializerOptions?)null);
    }

    private static ReviewOutcome Deserialize(string json)
    {
        ReviewOutcomeDto dto = JsonSerializer.Deserialize<ReviewOutcomeDto>(json, (JsonSerializerOptions?)null)
            ?? throw new UnreachableException("Stored review_outcome JSON deserialised to null.");

        return ReviewOutcome.FromDiscriminator(dto.Discriminator, dto.Reason)
            ?? throw new UnreachableException($"Unknown ReviewOutcome discriminator: '{dto.Discriminator}'");
    }

    private sealed record ReviewOutcomeDto(
        [property: JsonPropertyName("discriminator")] string Discriminator,
        [property: JsonPropertyName("reason")]
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? Reason);
}
