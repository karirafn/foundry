using Foundry.Modules.Monitoring.Domain.ValueObjects;

namespace Foundry.Modules.Monitoring.Infrastructure.RateBudget;

/// <summary>
/// Thread-safe in-memory store of the most recent rate-budget reading per provider budget key.
/// Registered as a singleton so both GitHub HTTP clients can write concurrently.
/// </summary>
internal interface IProviderRateBudget
{
    /// <summary>
    /// Records a new reading for <paramref name="key"/>, overwriting any prior value (last-writer-wins).
    /// </summary>
    void Record(ProviderBudgetKey key, RateBudgetReading reading);

    /// <summary>
    /// Returns the most recent reading for <paramref name="key"/>, or <c>null</c> if none has been recorded.
    /// </summary>
    RateBudgetReading? TryGet(ProviderBudgetKey key);

    /// <summary>
    /// Returns a snapshot of all currently recorded readings, keyed by <see cref="ProviderBudgetKey"/>.
    /// </summary>
    IReadOnlyDictionary<ProviderBudgetKey, RateBudgetReading> Snapshot();
}
