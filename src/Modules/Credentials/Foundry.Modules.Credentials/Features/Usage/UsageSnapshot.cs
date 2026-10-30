namespace Foundry.Modules.Credentials.Features.Usage;

/// <summary>
/// Point-in-time usage figures captured from the CLI <c>/usage</c> command.
/// Held in memory only — no table, no migration.
/// </summary>
internal sealed record UsageSnapshot(
    IReadOnlyList<UsageRateLimit> Limits,
    UsageExtraCredits? ExtraUsage,
    DateTimeOffset CapturedAt,
    string AccountEmail,
    DateTimeOffset? LastRefreshFailedAt);
