namespace Foundry.Modules.Credentials.Contracts;

/// <summary>
/// The plan usage meters of the first and the last <c>rate_limit_event</c> that carried them; both
/// <see langword="null"/> when no event did (an API-key account, or a CLI that does not report them).
/// </summary>
public sealed record PlanUsageWindows(PlanUsageMeters? First, PlanUsageMeters? Last)
{
    public static readonly PlanUsageWindows None = new(null, null);
}

/// <summary>
/// The plan's five-hour and seven-day utilization as fractions (1 is the limit); a window the event did not carry,
/// or carried malformed, is <see langword="null"/>.
/// </summary>
public sealed record PlanUsageMeters(decimal? FiveHour, decimal? SevenDay);
