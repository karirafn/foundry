namespace Foundry.Modules.Credentials.Contracts;

/// <summary>
/// Wire DTO for <c>GET /api/credentials/usage</c> and the <c>UsageSnapshotUpdated</c> hub push.
/// ASP.NET Core's default STJ serializer camelCases all property names on the wire.
/// <para>
/// A null <see cref="CapturedAt"/> means usage is unavailable: no poll has succeeded yet and the
/// last attempt (at <see cref="LastRefreshFailedAt"/>) failed. <see cref="Limits"/> is then empty
/// and <see cref="Source"/> null. This differs from 204 No Content, which means there is nothing to report.
/// </para>
/// <para>
/// <see cref="Source"/> names the CLI output the figures came from: <c>usageReport</c> (the
/// <c>/usage</c> report — Anthropic's own rows and severity) or <c>probeTurn</c> (the fallback
/// real turn — session and weekly windows only, Foundry-derived severity).
/// </para>
/// </summary>
public sealed record UsageSnapshotResponse(
    IReadOnlyList<UsageRateLimitResponse> Limits,
    UsageExtraCreditsResponse? ExtraUsage,
    DateTimeOffset? CapturedAt,
    string AccountEmail,
    DateTimeOffset? LastRefreshFailedAt,
    string? Source);

/// <summary>
/// One usage meter. <see cref="ResetsAt"/> is null when the server reports no reset time.
/// </summary>
public sealed record UsageRateLimitResponse(
    string Kind,
    decimal Percent,
    DateTimeOffset? ResetsAt,
    string? ModelDisplayName,
    string Severity,
    bool IsActive);

/// <summary>
/// The <c>extra_usage</c> block — extra-usage credit spend, in minor units of <see cref="Currency"/>.
/// <see cref="Percent"/> and <see cref="Severity"/> are server-computed (<c>critical</c> &gt;= 100,
/// <c>warning</c> &gt;= 80, else <c>normal</c>) and null when the plan has no positive monthly limit.
/// </summary>
public sealed record UsageExtraCreditsResponse(
    bool IsEnabled,
    long? MonthlyLimit,
    long UsedCredits,
    decimal? Utilization,
    string Currency,
    decimal? Percent,
    string? Severity);
