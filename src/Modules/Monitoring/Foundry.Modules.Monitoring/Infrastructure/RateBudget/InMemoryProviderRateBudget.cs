using System.Collections.Concurrent;

using Foundry.Modules.Monitoring.Domain.ValueObjects;

namespace Foundry.Modules.Monitoring.Infrastructure.RateBudget;

/// <summary>
/// Thread-safe, in-memory implementation of <see cref="IProviderRateBudget"/>.
/// The two GitHub HTTP clients write concurrently from different request threads;
/// <see cref="ConcurrentDictionary{TKey,TValue}"/> provides contention-free last-writer-wins semantics.
/// </summary>
internal sealed class InMemoryProviderRateBudget : IProviderRateBudget
{
    private readonly ConcurrentDictionary<ProviderBudgetKey, RateBudgetReading> _readings = new();

    public void Record(ProviderBudgetKey key, RateBudgetReading reading)
    {
        _readings[key] = reading;
    }

    public RateBudgetReading? TryGet(ProviderBudgetKey key)
    {
        _readings.TryGetValue(key, out RateBudgetReading? reading);
        return reading;
    }

    public IReadOnlyDictionary<ProviderBudgetKey, RateBudgetReading> Snapshot()
    {
        return _readings.ToDictionary(
            pair => pair.Key,
            pair => pair.Value);
    }
}
