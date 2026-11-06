namespace Foundry.Modules.Credentials.Features.Usage;

/// <summary>
/// The <c>extra_usage</c> block from the CLI <c>/usage</c> report — the extra-usage (overage)
/// spend for the billing period. Amounts are minor units of <see cref="Currency"/>
/// (<c>50000</c> USD = 500.00 USD). <see cref="Utilization"/>, when the server sends it, is
/// already on a 0–100 scale.
/// </summary>
internal sealed record UsageExtraCredits(
    bool IsEnabled,
    long? MonthlyLimit,
    long UsedCredits,
    decimal? Utilization,
    string Currency)
{
    private const decimal CriticalPercent = 100m;
    private const decimal WarningPercent = 80m;

    /// <summary>
    /// Share of the monthly limit spent, 0–100 (may exceed 100 when spend overruns the limit;
    /// clamping is the client's). <see langword="null"/> when the plan has no positive monthly
    /// limit — the credits are then amount-only. Derives from the server's
    /// <see cref="Utilization"/> when present, otherwise from <c>UsedCredits / MonthlyLimit</c>.
    /// </summary>
    internal decimal? Percent =>
        MonthlyLimit is not { } limit || limit <= 0
            ? null
            : Utilization ?? UsedCredits * 100m / limit;

    /// <summary>
    /// <c>critical</c> at or above 100 %, <c>warning</c> at or above 80 %, else <c>normal</c>;
    /// <see langword="null"/> when <see cref="Percent"/> is.
    /// </summary>
    internal string? Severity =>
        Percent switch
        {
            null => null,
            >= CriticalPercent => "critical",
            >= WarningPercent => "warning",
            _ => "normal",
        };
}
