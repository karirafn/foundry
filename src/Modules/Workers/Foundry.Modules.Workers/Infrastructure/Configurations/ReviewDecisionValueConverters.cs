using System.Text.Json.Nodes;

using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Verdicts;

using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Foundry.Modules.Workers.Infrastructure.Configurations;

/// <summary>
/// Stores a review run's <see cref="ReviewDecision"/> as one JSON object: the derived outcome, the reviewed commit and
/// patch-id, and the verdict in its schema's own shape, so the column reads the same as the run's structured_output.
/// </summary>
internal static class ReviewDecisionValueConverters
{
    private const string OutcomeProperty = "outcome";
    private const string ReviewedCommitProperty = "reviewedCommit";
    private const string PatchIdProperty = "patchId";
    private const string VerdictProperty = "verdict";

    // For an optional column: EF stores null without calling the converter, so the lambdas never see null.
    internal static readonly ValueConverter<ReviewDecision?, string> OptionalConverter = new(
        decision => Serialize(decision!),
        json => Deserialize(json));

    internal static readonly ValueComparer<ReviewDecision?> OptionalComparer = new(
        (left, right) => Equals(left, right),
        decision => decision == null ? 0 : decision.GetHashCode(),
        decision => decision);

    private static string Serialize(ReviewDecision decision) =>
        new JsonObject
        {
            [OutcomeProperty] = ReviewTokens.Outcome(decision.Outcome),
            [ReviewedCommitProperty] = decision.ReviewedCommit.Value,
            [PatchIdProperty] = decision.PatchId?.Value,
            [VerdictProperty] = ReviewVerdictParser.ToJson(decision.Verdict),
        }.ToJsonString();

    private static ReviewDecision Deserialize(string json)
    {
        JsonObject stored = JsonNode.Parse(json) as JsonObject
            ?? throw new InvalidOperationException("The stored review verdict is not a JSON object.");

        string outcomeToken = stored[OutcomeProperty]?.GetValue<string>()
            ?? throw new InvalidOperationException("The stored review verdict has no outcome.");
        string? patchId = stored[PatchIdProperty]?.GetValue<string>();

        return ReviewDecision.FromStored(
            stored[VerdictProperty] is JsonObject verdictNode && ReviewVerdictParser.TryParse(verdictNode) is { } verdict
                ? verdict
                : throw new InvalidOperationException("The stored review verdict does not parse."),
            ReviewTokens.Outcomes.TryGetValue(outcomeToken, out Contracts.ReviewVerdictOutcome outcome)
                ? outcome
                : throw new InvalidOperationException($"Unknown stored review outcome: {outcomeToken}"),
            CommitSha.From(stored[ReviewedCommitProperty]?.GetValue<string>()
                ?? throw new InvalidOperationException("The stored review verdict has no reviewed commit.")),
            patchId is null ? null : PatchId.From(patchId));
    }
}
