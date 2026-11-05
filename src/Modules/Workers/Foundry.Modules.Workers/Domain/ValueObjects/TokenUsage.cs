using Foundry.Modules.Workers.Contracts;

namespace Foundry.Modules.Workers.Domain.ValueObjects;

/// <summary>
/// Token counts for one run, split into the buckets the API bills separately. Every bucket is
/// nullable: <see langword="null"/> means unknown (no <c>modelUsage</c>, or a legacy row that
/// predates cache-column persistence), never zero.
/// </summary>
/// <param name="InputTokens">Uncached input tokens.</param>
/// <param name="CacheReadInputTokens">Input tokens read from the prompt cache.</param>
/// <param name="CacheCreationInputTokens">Input tokens written to the prompt cache.</param>
/// <param name="OutputTokens">Final output tokens.</param>
public sealed record TokenUsage(
    long? InputTokens,
    long? CacheReadInputTokens,
    long? CacheCreationInputTokens,
    long? OutputTokens)
{
    /// <summary>All buckets unknown.</summary>
    public static TokenUsage Unknown { get; } = new(null, null, null, null);

    /// <summary>
    /// Billable input tokens: uncached + cache-read + cache-write. This is the single place the
    /// input buckets are summed. <see langword="null"/> unless all three input buckets are known,
    /// so a partially-known run never reports an understated figure.
    /// </summary>
    public long? BillableInputTokens =>
        InputTokens is { } uncached
            && CacheReadInputTokens is { } cacheRead
            && CacheCreationInputTokens is { } cacheCreation
                ? uncached + cacheRead + cacheCreation
                : null;

    /// <summary>
    /// Sums one token metric across runs, counting known and unknown figures separately. This is
    /// the single place run-level token figures are aggregated: unknown runs are excluded from the
    /// total but reported in <see cref="TokenTotal.UnknownRunCount"/>, so a partial total is never
    /// presented as exact.
    /// </summary>
    public static TokenTotal Sum(IEnumerable<long?> values)
    {
        long total = 0;
        int known = 0;
        int unknown = 0;

        foreach (long? value in values)
        {
            if (value is { } figure)
            {
                total += figure;
                known++;
            }
            else
            {
                unknown++;
            }
        }

        return new TokenTotal(total, known, unknown);
    }
}
