using System.Text.Json;
using System.Text.Json.Nodes;

using Foundry.Modules.Workers.Domain.ValueObjects;

namespace Foundry.Modules.Workers.Features.Outcome;

/// <summary>
/// Reads the per-model token counts from a stream-json <c>result</c> event's <c>modelUsage</c>
/// object, summed across every model (the main model plus side-calls such as Haiku). Its
/// per-model <c>costUSD</c> sums to <c>total_cost_usd</c>, so tokens and cost reconcile.
/// </summary>
internal static class ModelUsageReader
{
    /// <summary>
    /// Returns the summed buckets, or <see cref="TokenUsage.Unknown"/> when <c>modelUsage</c> is
    /// absent, not an object or names no model. A bucket missing from any model is unknown.
    /// </summary>
    public static TokenUsage Read(JsonObject resultEvent)
    {
        ArgumentNullException.ThrowIfNull(resultEvent);

        if (resultEvent["modelUsage"] is not JsonObject models || models.Count == 0)
        {
            return TokenUsage.Unknown;
        }

        long? input = 0;
        long? cacheRead = 0;
        long? cacheCreation = 0;
        long? output = 0;

        foreach (KeyValuePair<string, JsonNode?> model in models)
        {
            JsonNode? usage = model.Value;
            input = Add(input, usage?["inputTokens"]);
            cacheRead = Add(cacheRead, usage?["cacheReadInputTokens"]);
            cacheCreation = Add(cacheCreation, usage?["cacheCreationInputTokens"]);
            output = Add(output, usage?["outputTokens"]);
        }

        return new TokenUsage(input, cacheRead, cacheCreation, output);
    }

    private static long? Add(long? total, JsonNode? node)
    {
        if (total is null
            || node is null
            || node.GetValueKind() != JsonValueKind.Number
            || !node.AsValue().TryGetValue(out long value)
            || value < 0)
        {
            return null;
        }

        return total + value;
    }
}
