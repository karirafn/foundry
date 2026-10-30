namespace Foundry.Modules.Credentials.Features.Usage;

/// <summary>
/// The <c>extra_usage</c> block from the CLI /usage output — prepaid credit balance.
/// </summary>
internal sealed record UsageExtraCredits(
    bool IsEnabled,
    long MonthlyLimit,
    long UsedCredits,
    decimal Utilization,
    string Currency);
