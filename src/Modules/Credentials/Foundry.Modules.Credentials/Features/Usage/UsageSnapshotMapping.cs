using Foundry.Modules.Credentials.Contracts;

namespace Foundry.Modules.Credentials.Features.Usage;

/// <summary>
/// Maps <see cref="UsageSnapshot"/> domain records to the public wire DTO
/// <see cref="UsageSnapshotResponse"/>. Internal so the WebApi host can access it
/// via <c>InternalsVisibleTo</c> — step 9's SignalR broadcaster reuses this mapper.
/// </summary>
internal static class UsageSnapshotMapping
{
    internal static UsageSnapshotResponse ToResponse(this UsageSnapshot snapshot) =>
        new(
            Limits: [..snapshot.Limits.Select(ToResponse)],
            ExtraUsage: snapshot.ExtraUsage?.ToResponse(),
            CapturedAt: snapshot.CapturedAt,
            AccountEmail: snapshot.AccountEmail,
            LastRefreshFailedAt: snapshot.LastRefreshFailedAt);

    private static UsageRateLimitResponse ToResponse(UsageRateLimit limit) =>
        new(
            Kind: limit.Kind,
            Percent: limit.Percent,
            ResetsAt: limit.ResetsAt,
            ModelDisplayName: limit.ModelDisplayName,
            Severity: limit.Severity,
            IsActive: limit.IsActive);

    private static UsageExtraCreditsResponse ToResponse(this UsageExtraCredits extra) =>
        new(
            IsEnabled: extra.IsEnabled,
            MonthlyLimit: extra.MonthlyLimit,
            UsedCredits: extra.UsedCredits,
            Utilization: extra.Utilization,
            Currency: extra.Currency);
}
