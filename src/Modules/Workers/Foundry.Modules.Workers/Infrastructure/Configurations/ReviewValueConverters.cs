using System.Text.Json;
using System.Text.Json.Serialization;

using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Shared;

using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Foundry.Modules.Workers.Infrastructure.Configurations;

/// <summary>
/// Stores a review run's supplied facts and its recorded verdict as JSON (ADR 0114), with enum values in the verdict
/// schema's own snake_case tokens so the column reads like the run's <c>structured_output</c>. Both values are
/// immutable, so a snapshot is the value itself.
/// </summary>
internal static class ReviewValueConverters
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false) },
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    // EF stores null without calling the converter, so the lambdas never see null.
    internal static readonly ValueConverter<SuppliedReviewFacts?, string> SuppliedFactsConverter = new(
        facts => SerializeFacts(facts!),
        json => DeserializeFacts(json));

    internal static readonly ValueComparer<SuppliedReviewFacts?> SuppliedFactsComparer = new(
        (left, right) => Equals(left, right),
        facts => facts == null ? 0 : facts.GetHashCode(),
        facts => facts);

    internal static readonly ValueConverter<RecordedReviewVerdict?, string> VerdictConverter = new(
        verdict => JsonSerializer.Serialize(verdict, JsonOptions),
        json => DeserializeVerdict(json));

    internal static readonly ValueComparer<RecordedReviewVerdict?> VerdictComparer = new(
        (left, right) => Equals(left, right),
        verdict => verdict == null ? 0 : verdict.GetHashCode(),
        verdict => verdict);

    internal static string SerializeFacts(SuppliedReviewFacts facts) =>
        JsonSerializer.Serialize(
            new StoredFacts(
                facts.PullRequestUrl.Value,
                facts.ReviewedCommit.Value,
                facts.PatchId?.Value,
                [.. facts.ClassifiedPaths.Select(path => new StoredPath(path.Path.Status, path.Path.Path, path.Class))],
                [.. facts.Criteria.Select(criterion => new StoredCriterion(criterion.Number, criterion.Text))]),
            JsonOptions);

    internal static SuppliedReviewFacts DeserializeFacts(string json)
    {
        StoredFacts stored = JsonSerializer.Deserialize<StoredFacts>(json, JsonOptions)
            ?? throw new InvalidOperationException("Failed to deserialize the supplied review facts.");

        return new SuppliedReviewFacts(
            PullRequestUrl.From(stored.PullRequestUrl),
            CommitSha.From(stored.ReviewedCommit),
            stored.PatchId is null ? null : PatchId.From(stored.PatchId),
            [.. stored.Paths.Select(path => new ClassifiedPath(new ChangedPath(path.Status, path.Path), path.Class))],
            [.. stored.Criteria.Select(criterion => new AcceptanceCriterion(criterion.Number, criterion.Text))]);
    }

    private static RecordedReviewVerdict DeserializeVerdict(string json) =>
        JsonSerializer.Deserialize<RecordedReviewVerdict>(json, JsonOptions)
            ?? throw new InvalidOperationException("Failed to deserialize the review verdict.");

    private sealed record StoredFacts(
        string PullRequestUrl,
        string ReviewedCommit,
        string? PatchId,
        IReadOnlyList<StoredPath> Paths,
        IReadOnlyList<StoredCriterion> Criteria);

    private sealed record StoredPath(ChangeStatus Status, string Path, ChangedFileClass Class);

    private sealed record StoredCriterion(int Number, string Text);
}
