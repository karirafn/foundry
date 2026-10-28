using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;

using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Foundry.Modules.Issues.Infrastructure.Configurations;

internal static class RevisionTriggerJsonConversion
{
    public static ValueConverter<RevisionTrigger, string> Converter { get; } = new(
        trigger => Serialize(trigger),
        json => Deserialize(json));

    public static ValueComparer<RevisionTrigger> Comparer { get; } = new(
        (a, b) => a != null && b != null && a.Discriminator == b.Discriminator && TriggersAreEqual(a, b),
        trigger => trigger.GetHashCode(),
        trigger => trigger);

    private static string Serialize(RevisionTrigger trigger)
    {
        RevisionTriggerDto dto = trigger switch
        {
            RevisionTrigger.ReviewFeedback rf => new RevisionTriggerDto(
                trigger.Discriminator,
                [.. rf.Comments.Select(c => new ReviewCommentDto(c.Body, c.FilePath, c.Line))],
                rf.OmittedCommentCount,
                rf.NewestConsumedCommentAt,
                null),
            RevisionTrigger.FailedChecks fc => new RevisionTriggerDto(
                trigger.Discriminator,
                null,
                null,
                null,
                [.. fc.FailingChecks.Select(c => new FailingCheckDto(c.Name, c.Conclusion, c.DetailsUrl))]),
            _ => throw new UnreachableException($"Unknown RevisionTrigger discriminator: '{trigger.Discriminator}'"),
        };

        return JsonSerializer.Serialize(dto, (JsonSerializerOptions?)null);
    }

    private static RevisionTrigger Deserialize(string? json)
    {
        if (json is null)
        {
            return new RevisionTrigger.ReviewFeedback([], 0, null);
        }

        RevisionTriggerDto dto = JsonSerializer.Deserialize<RevisionTriggerDto>(json, (JsonSerializerOptions?)null)
            ?? new RevisionTriggerDto("review_feedback", [], 0, null, null);

        return dto.Discriminator switch
        {
            "review_feedback" => new RevisionTrigger.ReviewFeedback(
                [.. (dto.Comments ?? []).Select(c => new ReviewComment(c.Body, c.FilePath, c.Line))],
                dto.OmittedCommentCount ?? 0,
                dto.NewestConsumedCommentAt),
            "failed_checks" => new RevisionTrigger.FailedChecks(
                [.. (dto.FailingChecks ?? []).Select(c => new FailingCheck(c.Name, c.Conclusion, c.DetailsUrl))]),
            _ => throw new UnreachableException($"Unknown RevisionTrigger discriminator: '{dto.Discriminator}'"),
        };
    }

    private static bool TriggersAreEqual(RevisionTrigger a, RevisionTrigger b) =>
        (a, b) switch
        {
            (RevisionTrigger.ReviewFeedback rfA, RevisionTrigger.ReviewFeedback rfB) =>
                rfA.OmittedCommentCount == rfB.OmittedCommentCount
                && rfA.NewestConsumedCommentAt == rfB.NewestConsumedCommentAt
                && rfA.Comments.SequenceEqual(rfB.Comments),
            (RevisionTrigger.FailedChecks fcA, RevisionTrigger.FailedChecks fcB) =>
                fcA.FailingChecks.SequenceEqual(fcB.FailingChecks),
            _ => false,
        };

    private sealed record RevisionTriggerDto(
        [property: JsonPropertyName("discriminator")] string Discriminator,
        [property: JsonPropertyName("comments")] IReadOnlyList<ReviewCommentDto>? Comments,
        [property: JsonPropertyName("omittedCommentCount")] int? OmittedCommentCount,
        [property: JsonPropertyName("newestConsumedCommentAt")] DateTimeOffset? NewestConsumedCommentAt,
        [property: JsonPropertyName("failingChecks")] IReadOnlyList<FailingCheckDto>? FailingChecks);

    private sealed record ReviewCommentDto(
        [property: JsonPropertyName("body")] string Body,
        [property: JsonPropertyName("filePath")] string? FilePath,
        [property: JsonPropertyName("line")] int? Line);

    private sealed record FailingCheckDto(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("conclusion")] string Conclusion,
        [property: JsonPropertyName("detailsUrl")] string? DetailsUrl);
}
