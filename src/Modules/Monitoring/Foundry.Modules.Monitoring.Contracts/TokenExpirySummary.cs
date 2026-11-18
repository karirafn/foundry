namespace Foundry.Modules.Monitoring.Contracts;

/// <summary>
/// An account token's expiry as the server judges it now. <see cref="Status"/> is one of
/// <c>notChecked</c>, <c>unavailable</c>, <c>never</c>, <c>current</c>, <c>expiringSoon</c> or
/// <c>expired</c>. <see cref="ExpiresAt"/> is set for <c>current</c>, <c>expiringSoon</c> and
/// <c>expired</c>, and null otherwise.
/// </summary>
public sealed record TokenExpirySummary(string Status, DateTimeOffset? ExpiresAt);
