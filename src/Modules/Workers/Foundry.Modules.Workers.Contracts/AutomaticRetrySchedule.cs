namespace Foundry.Modules.Workers.Contracts;

/// <summary>
/// How a <see cref="FailureCategory"/> retries automatically: one backoff per automatic retry, so the
/// number of backoffs is the maximum streak of consecutive auto-retryable failed runs before the issue
/// waits for an operator.
/// </summary>
public sealed class AutomaticRetrySchedule
{
    private readonly TimeSpan[] _backoffs;

    private AutomaticRetrySchedule(TimeSpan[] backoffs) => _backoffs = backoffs;

    public int MaxRetries => _backoffs.Length;

    public IReadOnlyList<TimeSpan> Backoffs => _backoffs;

    public static AutomaticRetrySchedule Create(params TimeSpan[] backoffs)
    {
        ArgumentNullException.ThrowIfNull(backoffs);

        if (backoffs.Length == 0)
        {
            throw new ArgumentException("An automatic retry schedule needs at least one backoff.", nameof(backoffs));
        }

        if (backoffs.Any(backoff => backoff <= TimeSpan.Zero))
        {
            throw new ArgumentException("Every automatic retry backoff must be positive.", nameof(backoffs));
        }

        return new AutomaticRetrySchedule([.. backoffs]);
    }

    /// <summary>
    /// Whether a streak of <paramref name="consecutiveFailures"/> auto-retryable failed runs has used up the schedule.
    /// </summary>
    public bool IsExhausted(int consecutiveFailures) => consecutiveFailures >= MaxRetries;

    /// <summary>
    /// The wait after the latest failure before the next automatic retry, given the streak of
    /// consecutive auto-retryable failed runs ending with it. A streak of 0 (the failed run is not
    /// yet visible) is treated as the first failure.
    /// </summary>
    public TimeSpan BackoffAfter(int consecutiveFailures) =>
        _backoffs[Math.Clamp(consecutiveFailures - 1, 0, _backoffs.Length - 1)];
}
