namespace Foundry.Modules.Credentials.Contracts;

/// <summary>
/// Wire DTO for <c>GET /api/credentials/usage</c>.
/// ASP.NET Core's default STJ serializer camelCases all property names on the wire.
/// <para>
/// A null <see cref="CapturedAt"/> means usage is unavailable: no poll has succeeded yet and the
/// last attempt (at <see cref="LastRefreshFailedAt"/>) failed. <see cref="Limits"/> is then empty.
/// This differs from 204 No Content, which means there is nothing to report.
/// </para>
/// </summary>
public sealed record UsageSnapshotResponse(
    IReadOnlyList<UsageRateLimitResponse> Limits,
    UsageExtraCreditsResponse? ExtraUsage,
    DateTimeOffset? CapturedAt,
    string AccountEmail,
    DateTimeOffset? LastRefreshFailedAt);

/// <summary>
/// One window entry from <c>rate_limit_info.unifiedWindows</c> in a CLI <c>rate_limit_event</c>.
/// </summary>
public sealed record UsageRateLimitResponse(
    string Kind,
    decimal Percent,
    DateTimeOffset ResetsAt,
    string? ModelDisplayName,
    string Severity,
    bool IsActive);

/// <summary>
/// The <c>extra_usage</c> block — prepaid credit balance.
/// </summary>
public sealed record UsageExtraCreditsResponse(
    bool IsEnabled,
    long MonthlyLimit,
    long UsedCredits,
    decimal Utilization,
    string Currency);
