using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

using Foundry.Modules.Issues.Domain.ValueObjects;

using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Foundry.Modules.Issues.Infrastructure.Configurations;

/// <summary>
/// Maps <see cref="ChecksPendingReason"/> to the flat JSON <c>{"discriminator":"head_moved"}</c> stored in the
/// <c>checks_pending_reason</c> column, the same shape <see cref="RebaseOutcomeJsonConversion"/> stores.
/// </summary>
internal static class ChecksPendingReasonJsonConversion
{
    public static ValueConverter<ChecksPendingReason, string> Converter { get; } = new(
        reason => Serialize(reason),
        json => Deserialize(json));

    public static ValueComparer<ChecksPendingReason> Comparer { get; } = new(
        (a, b) => a == b,
        reason => reason.GetHashCode(),
        reason => reason);

    private static string Serialize(ChecksPendingReason reason) =>
        JsonSerializer.Serialize(new ChecksPendingReasonDto(reason.Discriminator), (JsonSerializerOptions?)null);

    private static ChecksPendingReason Deserialize(string json)
    {
        ChecksPendingReasonDto dto = JsonSerializer.Deserialize<ChecksPendingReasonDto>(json, (JsonSerializerOptions?)null)
            ?? throw new UnreachableException("Stored checks_pending_reason JSON deserialised to null.");

        return ChecksPendingReason.FromDiscriminator(dto.Discriminator)
            ?? throw new UnreachableException($"Unknown ChecksPendingReason discriminator: '{dto.Discriminator}'");
    }

    private sealed record ChecksPendingReasonDto(
        [property: JsonPropertyName("discriminator")] string Discriminator);
}
