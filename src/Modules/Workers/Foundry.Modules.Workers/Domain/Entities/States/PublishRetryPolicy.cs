namespace Foundry.Modules.Workers.Domain.Entities.States;

/// <summary>How often, and how long apart, Foundry retries a transiently failed publish (ADR 0100).</summary>
public static class PublishRetryPolicy
{
    /// <summary>The wait before each retry: the first retry follows the first failed attempt by the first delay.</summary>
    public static readonly IReadOnlyList<TimeSpan> RetryDelays =
    [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(2),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15),
    ];

    /// <summary>Publish attempts before the run waits for an operator's Retry publish: the first plus every retry.</summary>
    public static int MaxAttempts => RetryDelays.Count + 1;

    /// <summary>
    /// How long an unpublished run's Hand-off volume is kept. After it the volume is deleted and the run discarded, so a
    /// publish nobody retries cannot hold a volume forever.
    /// </summary>
    public static readonly TimeSpan HandOffTimeToLive = TimeSpan.FromDays(7);
}
