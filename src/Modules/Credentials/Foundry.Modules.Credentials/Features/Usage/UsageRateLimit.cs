namespace Foundry.Modules.Credentials.Features.Usage;

/// <summary>
/// One window entry from <c>rate_limit_info.unifiedWindows</c> in a CLI <c>rate_limit_event</c>.
/// </summary>
internal sealed record UsageRateLimit(
    string Kind,
    decimal Percent,
    DateTimeOffset ResetsAt,
    string? ModelDisplayName,
    string Severity,
    bool IsActive);
