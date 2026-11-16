using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

using Foundry.Modules.Issues.Domain.ValueObjects;

using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Foundry.Modules.Issues.Infrastructure.Configurations;

/// <summary>
/// Maps <see cref="RebasePendingReason"/> to the flat JSON <c>{"discriminator":"catch_up"}</c> stored in the
/// <c>rebase_pending_reason</c> column, the same shape <see cref="RebaseOutcomeJsonConversion"/> stores.
/// </summary>
internal static class RebasePendingReasonJsonConversion
{
    public static ValueConverter<RebasePendingReason, string> Converter { get; } = new(
        reason => Serialize(reason),
        json => Deserialize(json));

    public static ValueComparer<RebasePendingReason> Comparer { get; } = new(
        (a, b) => a == b,
        reason => reason.GetHashCode(),
        reason => reason);

    private static string Serialize(RebasePendingReason reason) =>
        JsonSerializer.Serialize(new RebasePendingReasonDto(reason.Discriminator), (JsonSerializerOptions?)null);

    private static RebasePendingReason Deserialize(string json)
    {
        RebasePendingReasonDto dto = JsonSerializer.Deserialize<RebasePendingReasonDto>(json, (JsonSerializerOptions?)null)
            ?? throw new UnreachableException("Stored rebase_pending_reason JSON deserialised to null.");

        return RebasePendingReason.FromDiscriminator(dto.Discriminator)
            ?? throw new UnreachableException($"Unknown RebasePendingReason discriminator: '{dto.Discriminator}'");
    }

    private sealed record RebasePendingReasonDto(
        [property: JsonPropertyName("discriminator")] string Discriminator);
}
