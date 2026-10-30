namespace Foundry.Modules.Credentials.Features.Usage;

/// <summary>
/// One entry from <c>usage_report.rate_limits.limits[]</c> in the CLI /usage output.
/// </summary>
internal sealed record UsageRateLimit(
    string Kind,
    decimal Percent,
    DateTimeOffset ResetsAt,
    string? ModelDisplayName,
    string Severity,
    bool IsActive);
