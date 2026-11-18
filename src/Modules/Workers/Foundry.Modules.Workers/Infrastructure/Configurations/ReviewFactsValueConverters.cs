using System.Text.Json;

using Foundry.Modules.Workers.Domain.ValueObjects;

using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Foundry.Modules.Workers.Infrastructure.Configurations;

/// <summary>
/// Stores a run's <see cref="ReviewFacts"/> as one JSON object. Each path keeps the class it was given at dispatch, so a
/// later change to the classifier never changes the facts a stored run is checked against.
/// </summary>
internal static class ReviewFactsValueConverters
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // For an optional column: EF stores null without calling the converter, so the lambdas never see null.
    internal static readonly ValueConverter<ReviewFacts?, string> OptionalConverter = new(
        facts => Serialize(facts!),
        json => Deserialize(json));

    internal static readonly ValueComparer<ReviewFacts?> OptionalComparer = new(
        (left, right) => Equals(left, right),
        facts => facts == null ? 0 : facts.GetHashCode(),
        facts => facts);

    private static string Serialize(ReviewFacts facts) =>
        JsonSerializer.Serialize(
            new StoredReviewFacts(
                facts.PullRequestUrl.Value,
                facts.ReviewedCommit.Value,
                facts.PatchId?.Value,
                [.. facts.ClassifiedPaths.Select(path =>
                    new StoredClassifiedPath(StatusToken(path.Status), path.Path, ClassToken(path.Class)))],
                [.. facts.Criteria.Items.Select(criterion => new StoredCriterion(criterion.Id, criterion.Text))]),
            JsonOptions);

    private static ReviewFacts Deserialize(string json)
    {
        StoredReviewFacts stored = JsonSerializer.Deserialize<StoredReviewFacts>(json, JsonOptions)
            ?? throw new InvalidOperationException("Failed to deserialize the supplied review facts.");

        return new ReviewFacts(
            PullRequestUrl.From(stored.PullRequestUrl),
            CommitSha.From(stored.ReviewedCommit),
            stored.PatchId is null ? null : PatchId.From(stored.PatchId),
            [.. stored.Paths.Select(path =>
                new ClassifiedPath(StatusFromToken(path.Status), path.Path, ClassFromToken(path.Class)))],
            AcceptanceCriteria.FromItems([.. stored.Criteria.Select(criterion =>
                new AcceptanceCriterion(criterion.Id, criterion.Text))]));
    }

    // The status letters git prints, as the Publisher reports them.
    private static string StatusToken(ChangeStatus status) => status switch
    {
        ChangeStatus.Added => "A",
        ChangeStatus.Modified => "M",
        ChangeStatus.Deleted => "D",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown change status."),
    };

    private static ChangeStatus StatusFromToken(string token) => token switch
    {
        "A" => ChangeStatus.Added,
        "M" => ChangeStatus.Modified,
        "D" => ChangeStatus.Deleted,
        _ => throw new InvalidOperationException($"Unknown stored change status: {token}"),
    };

    private static string ClassToken(ChangedFileClass changedFileClass) => changedFileClass switch
    {
        ChangedFileClass.Production => "production",
        ChangedFileClass.Test => "test",
        ChangedFileClass.Other => "other",
        _ => throw new ArgumentOutOfRangeException(nameof(changedFileClass), changedFileClass, "Unknown changed-file class."),
    };

    private static ChangedFileClass ClassFromToken(string token) => token switch
    {
        "production" => ChangedFileClass.Production,
        "test" => ChangedFileClass.Test,
        "other" => ChangedFileClass.Other,
        _ => throw new InvalidOperationException($"Unknown stored changed-file class: {token}"),
    };

    private sealed record StoredReviewFacts(
        string PullRequestUrl,
        string ReviewedCommit,
        string? PatchId,
        IReadOnlyList<StoredClassifiedPath> Paths,
        IReadOnlyList<StoredCriterion> Criteria);

    private sealed record StoredClassifiedPath(string Status, string Path, string Class);

    private sealed record StoredCriterion(string Id, string Text);
}
