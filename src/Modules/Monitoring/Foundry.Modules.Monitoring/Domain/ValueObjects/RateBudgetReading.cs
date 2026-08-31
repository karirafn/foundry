namespace Foundry.Modules.Monitoring.Domain.ValueObjects;

/// <summary>
/// The last observed rate-limit headroom for one provider budget.
/// </summary>
/// <param name="Remaining">Remaining API calls/points in the current reset window.</param>
/// <param name="Limit">Total budget for the window, when reported by the provider.</param>
/// <param name="ResetAt">
/// Provider-reported timestamp at which the window resets (display-only — the policy keys on
/// <see cref="ObservedAt"/>, never on this value, to avoid timestamp-vs-duration confusion).
/// </param>
/// <param name="ObservedAt">When this reading was recorded.</param>
internal sealed record RateBudgetReading(
    int Remaining,
    int? Limit,
    DateTimeOffset? ResetAt,
    DateTimeOffset ObservedAt)
{
    /// <summary>
    /// The duration after which a reading is considered stale and degrades to
    /// <see cref="ProviderBudgetHealth.Unknown"/> rather than <see cref="ProviderBudgetHealth.Low"/>.
    /// Set to 1 hour — matching the GitHub REST and GraphQL reset window — so a reading cannot
    /// survive a full reset cycle without being refreshed.
    /// </summary>
    public static readonly TimeSpan StalenessWindow = TimeSpan.FromHours(1);

    /// <summary>
    /// Returns <c>true</c> when the elapsed time since <see cref="ObservedAt"/> exceeds
    /// <see cref="StalenessWindow"/>.
    /// </summary>
    public bool IsStale(DateTimeOffset now) => now - ObservedAt > StalenessWindow;
}
