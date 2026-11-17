namespace Foundry.Modules.Monitoring.Domain.ValueObjects;

/// <summary>A <see cref="TokenExpiry"/> judged at a point in time.</summary>
public enum TokenExpiryStatus
{
    NotChecked,
    Unavailable,
    Never,
    Current,
    ExpiringSoon,
    Expired,
}
