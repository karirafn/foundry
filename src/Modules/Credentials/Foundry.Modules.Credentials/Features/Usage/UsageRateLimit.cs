namespace Foundry.Modules.Credentials.Features.Usage;

/// <summary>
/// One usage meter: an entry of <c>usage_report.rate_limits.limits[]</c> (severity is Anthropic's)
/// or, on the probe-turn fallback, a window of <c>rate_limit_info.unifiedWindows</c> (severity is
/// Foundry's). <see cref="ResetsAt"/> is <see langword="null"/> when the server reports no reset time.
/// </summary>
internal sealed record UsageRateLimit(
    string Kind,
    decimal Percent,
    DateTimeOffset? ResetsAt,
    string? ModelDisplayName,
    string Severity,
    bool IsActive);
