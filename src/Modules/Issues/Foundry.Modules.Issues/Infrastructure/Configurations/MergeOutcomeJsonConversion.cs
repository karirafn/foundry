using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

using Foundry.Modules.Issues.Domain.ValueObjects;

using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Foundry.Modules.Issues.Infrastructure.Configurations;

/// <summary>
/// Maps <see cref="MergeOutcome"/> to the flat JSON <c>{"discriminator":"skipped","reason":"head_moved"}</c>
/// stored in the <c>merge_outcome</c> column. A <c>NULL</c> column (review issues that never entered
/// merge-pending) materialises as a <see langword="null"/> outcome.
/// </summary>
internal static class MergeOutcomeJsonConversion
{
    // EF Core never passes NULL through a converter on a nullable column, so the null-forgiving operators
    // only satisfy the nullable signature the property builder expects.
    public static ValueConverter<MergeOutcome?, string> Converter { get; } = new(
        outcome => Serialize(outcome!),
        json => Deserialize(json));

    public static ValueComparer<MergeOutcome?> Comparer { get; } = new(
        (a, b) => a == b,
        outcome => outcome == null ? 0 : outcome.GetHashCode(),
        outcome => outcome);

    private static string Serialize(MergeOutcome outcome)
    {
        MergeOutcomeDto dto = new(
            outcome.Discriminator,
            outcome is MergeOutcome.Skipped skipped ? skipped.Reason : null);

        return JsonSerializer.Serialize(dto, (JsonSerializerOptions?)null);
    }

    private static MergeOutcome Deserialize(string json)
    {
        MergeOutcomeDto dto = JsonSerializer.Deserialize<MergeOutcomeDto>(json, (JsonSerializerOptions?)null)
            ?? throw new UnreachableException("Stored merge_outcome JSON deserialised to null.");

        return MergeOutcome.FromDiscriminator(dto.Discriminator, dto.Reason)
            ?? throw new UnreachableException($"Unknown MergeOutcome discriminator: '{dto.Discriminator}'");
    }

    private sealed record MergeOutcomeDto(
        [property: JsonPropertyName("discriminator")] string Discriminator,
        [property: JsonPropertyName("reason")]
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? Reason);
}
