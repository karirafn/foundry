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
    /// Sums values across runs, propagating unknowns: the result is <see langword="null"/> when
    /// any value is <see langword="null"/> (or when there are no values), because an aggregate
    /// that silently skips unknown runs would understate usage.
    /// </summary>
    public static long? SumKnown(IEnumerable<long?> values)
    {
        long total = 0;
        bool any = false;

        foreach (long? value in values)
        {
            if (value is not { } known)
            {
                return null;
            }

            total += known;
            any = true;
        }

        return any ? total : null;
    }
}
