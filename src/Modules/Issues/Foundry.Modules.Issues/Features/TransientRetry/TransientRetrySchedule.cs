namespace Foundry.Modules.Issues.Features.TransientRetry;

/// <summary>
/// Shared constants and backoff policy for transient API error retries.
/// </summary>
internal static class TransientRetrySchedule
{
    internal const int MaxTransientRetries = 2;
    internal static readonly TimeSpan InitialBackoff = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Computes the exponential backoff for a given attempt count.
    /// <paramref name="attempt"/> is the number of prior consecutive transient runs (0-based):
    /// 0 prior runs = first retry, 1 prior run = second retry. Both use 1-minute backoff;
    /// at 2+ runs (capped by <see cref="MaxTransientRetries"/>) no retry is issued.
    /// </summary>
    internal static TimeSpan ComputeBackoff(int attempt)
    {
        // exponent = max(0, attempt-1): attempt 0 or 1 → exponent 0 → 1min * 2^0 = 1 minute.
        // Doubling applies if MaxTransientRetries > 2 (e.g., attempt 2 → 2min).
        int exponent = Math.Max(0, attempt - 1);
        return TimeSpan.FromTicks(InitialBackoff.Ticks * (1L << exponent));
    }
}
