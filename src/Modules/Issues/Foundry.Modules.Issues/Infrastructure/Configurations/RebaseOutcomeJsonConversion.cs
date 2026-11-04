using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

using Foundry.Modules.Issues.Domain.ValueObjects;

using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Foundry.Modules.Issues.Infrastructure.Configurations;

/// <summary>
/// Maps <see cref="RebaseOutcome"/> to the flat JSON <c>{"discriminator":"skipped","reason":"timeout"}</c>
/// stored in the <c>rebase_outcome</c> column. A <c>NULL</c> column (issues that passed the checks gate
/// before the rebase step existed) materialises as a <see langword="null"/> outcome.
/// </summary>
internal static class RebaseOutcomeJsonConversion
{
    // EF Core never passes NULL through a converter on a nullable column, so the null-forgiving operators
    // only satisfy the nullable signature the property builder expects.
    public static ValueConverter<RebaseOutcome?, string> Converter { get; } = new(
        outcome => Serialize(outcome!),
        json => Deserialize(json));

    public static ValueComparer<RebaseOutcome?> Comparer { get; } = new(
        (a, b) => a == b,
        outcome => outcome == null ? 0 : outcome.GetHashCode(),
        outcome => outcome);

    private static string Serialize(RebaseOutcome outcome)
    {
        RebaseOutcomeDto dto = new(
            outcome.Discriminator,
            outcome is RebaseOutcome.Skipped skipped ? skipped.Reason : null);

        return JsonSerializer.Serialize(dto, (JsonSerializerOptions?)null);
    }

    private static RebaseOutcome Deserialize(string json)
    {
        RebaseOutcomeDto dto = JsonSerializer.Deserialize<RebaseOutcomeDto>(json, (JsonSerializerOptions?)null)
            ?? throw new UnreachableException("Stored rebase_outcome JSON deserialised to null.");

        return RebaseOutcome.FromDiscriminator(dto.Discriminator, dto.Reason)
            ?? throw new UnreachableException($"Unknown RebaseOutcome discriminator: '{dto.Discriminator}'");
    }

    private sealed record RebaseOutcomeDto(
        [property: JsonPropertyName("discriminator")] string Discriminator,
        [property: JsonPropertyName("reason")]
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? Reason);
}
