namespace Foundry.Modules.Monitoring.Domain.ValueObjects;

/// <summary>
/// Pure evaluation of a rate-budget reading against a floor.
/// No side effects — all inputs are parameters; the verdict is the return value.
/// </summary>
internal static class ProviderBudgetPolicy
{
    /// <summary>
    /// The default floor for GitHub REST and GraphQL budgets.
    /// GitHub grants 5,000 requests/points per hour; 500 (10 %) reserves roughly 6 minutes of
    /// uninterrupted polling headroom while giving the dashboard time to surface a Low verdict
    /// before the budget is exhausted.
    /// </summary>
    internal const int DefaultFloor = 500;

    /// <summary>
    /// Evaluates <paramref name="reading"/> against <paramref name="floor"/> at <paramref name="now"/>.
    /// </summary>
    /// <param name="reading">
    /// The last observed reading, or <c>null</c> if no reading has been recorded yet.
    /// </param>
    /// <param name="floor">Minimum acceptable <see cref="RateBudgetReading.Remaining"/> value.</param>
    /// <param name="now">The current wall-clock time, used to assess staleness.</param>
    /// <returns>
    /// <list type="bullet">
    ///   <item><see cref="ProviderBudgetHealth.Unknown"/> — reading absent or stale (fail open).</item>
    ///   <item><see cref="ProviderBudgetHealth.Healthy"/> — fresh reading with remaining &gt;= floor.</item>
    ///   <item><see cref="ProviderBudgetHealth.Low"/> — fresh reading with remaining &lt; floor.</item>
    /// </list>
    /// </returns>
    internal static ProviderBudgetHealth Evaluate(RateBudgetReading? reading, int floor, DateTimeOffset now)
    {
        if (reading is null)
        {
            return ProviderBudgetHealth.Unknown;
        }

        if (reading.IsStale(now))
        {
            return ProviderBudgetHealth.Unknown;
        }

        return reading.Remaining >= floor
            ? ProviderBudgetHealth.Healthy
            : ProviderBudgetHealth.Low;
    }
}
