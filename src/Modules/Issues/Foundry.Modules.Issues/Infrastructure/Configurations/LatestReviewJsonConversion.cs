using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Workers.Contracts;

using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Foundry.Modules.Issues.Infrastructure.Configurations;

/// <summary>
/// Maps <see cref="LatestReview"/> to the JSON <c>{"run_id":"…","outcome":"approved","patch_id":"…"}</c> stored in the
/// <c>latest_review</c> column, with the outcome in the review verdict's snake_case tokens. A <c>NULL</c> column (an
/// issue no review completed for) materialises as <see langword="null"/>.
/// </summary>
internal static class LatestReviewJsonConversion
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false) },
    };

    // EF Core never passes NULL through a converter on a nullable column, so the null-forgiving operator only
    // satisfies the nullable signature the property builder expects.
    public static ValueConverter<LatestReview?, string> Converter { get; } = new(
        latestReview => Serialize(latestReview!),
        json => Deserialize(json));

    public static ValueComparer<LatestReview?> Comparer { get; } = new(
        (a, b) => a == b,
        latestReview => latestReview == null ? 0 : latestReview.GetHashCode(),
        latestReview => latestReview);

    private static string Serialize(LatestReview latestReview) =>
        JsonSerializer.Serialize(
            new LatestReviewDto(latestReview.RunId.Value, latestReview.Outcome, latestReview.PatchId),
            JsonOptions);

    private static LatestReview Deserialize(string json)
    {
        LatestReviewDto dto = JsonSerializer.Deserialize<LatestReviewDto>(json, JsonOptions)
            ?? throw new UnreachableException("Stored latest_review JSON deserialised to null.");

        return new LatestReview(WorkerRunId.From(dto.RunId), dto.Outcome, dto.PatchId);
    }

    private sealed record LatestReviewDto(
        [property: JsonPropertyName("run_id")] Guid RunId,
        [property: JsonPropertyName("outcome")] ReviewVerdictOutcome Outcome,
        [property: JsonPropertyName("patch_id")] string? PatchId);
}
