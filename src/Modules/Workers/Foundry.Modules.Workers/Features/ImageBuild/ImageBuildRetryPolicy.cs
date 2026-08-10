namespace Foundry.Modules.Workers.Features.ImageBuild;

/// <summary>
/// Computes the exponential backoff delay for image build retries.
/// </summary>
internal sealed record ImageBuildRetryPolicy(TimeSpan InitialBackoff, TimeSpan MaxBackoff)
{
    /// <summary>
    /// Computes the backoff delay for the given attempt number.
    /// Formula: <c>InitialBackoff * 2^(attempt-1)</c>, capped at <see cref="MaxBackoff"/>.
    /// Attempts &lt;= 0 are clamped to 1, yielding the initial backoff.
    /// </summary>
    public TimeSpan ComputeBackoff(int attempt)
    {
        int clampedAttempt = Math.Max(1, attempt);

        // Use double arithmetic to avoid overflow on large attempt counts.
        // Guard against +Infinity or values that would overflow TimeSpan before converting.
        double multiplier = Math.Pow(2, clampedAttempt - 1);
        double seconds = InitialBackoff.TotalSeconds * multiplier;
        if (double.IsInfinity(seconds) || seconds >= MaxBackoff.TotalSeconds)
        {
            return MaxBackoff;
        }

        TimeSpan uncapped = TimeSpan.FromSeconds(seconds);
        return uncapped < MaxBackoff ? uncapped : MaxBackoff;
    }
}
