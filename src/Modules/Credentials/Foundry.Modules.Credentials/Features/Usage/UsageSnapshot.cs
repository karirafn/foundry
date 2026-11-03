namespace Foundry.Modules.Credentials.Features.Usage;

/// <summary>
/// Point-in-time usage figures captured from the CLI <c>/usage</c> command.
/// Held in memory only — no table, no migration.
/// <para>
/// <see cref="CapturedAt"/> is <see langword="null"/> when no poll has ever succeeded but the last
/// attempt failed (see <see cref="IsUnavailable"/>): the snapshot then carries no figures, only the
/// account and <see cref="LastRefreshFailedAt"/>, so the failure is visible rather than silent.
/// </para>
/// </summary>
internal sealed record UsageSnapshot(
    IReadOnlyList<UsageRateLimit> Limits,
    UsageExtraCredits? ExtraUsage,
    DateTimeOffset? CapturedAt,
    string AccountEmail,
    DateTimeOffset? LastRefreshFailedAt)
{
    /// <summary>True when no poll has succeeded yet and the last attempt failed.</summary>
    internal bool IsUnavailable => CapturedAt is null;

    /// <summary>Creates the "attempted and failed before any success" snapshot.</summary>
    internal static UsageSnapshot Unavailable(string accountEmail, DateTimeOffset failedAt) =>
        new(Limits: [], ExtraUsage: null, CapturedAt: null, AccountEmail: accountEmail, LastRefreshFailedAt: failedAt);
}
