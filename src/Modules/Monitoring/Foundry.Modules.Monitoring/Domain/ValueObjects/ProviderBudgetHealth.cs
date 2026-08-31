namespace Foundry.Modules.Monitoring.Domain.ValueObjects;

/// <summary>
/// The evaluated headroom verdict for a provider API rate budget.
/// Derived by <see cref="ProviderBudgetPolicy"/> from a <see cref="RateBudgetReading"/> and a floor.
/// </summary>
internal enum ProviderBudgetHealth
{
    /// <summary>Remaining headroom is at or above the floor and the reading is fresh.</summary>
    Healthy,

    /// <summary>Remaining headroom is below the floor and the reading is fresh.</summary>
    Low,

    /// <summary>
    /// No reading is available or the reading is stale.
    /// The policy fails open — absence of a signal is never treated as exhaustion.
    /// </summary>
    Unknown,
}
