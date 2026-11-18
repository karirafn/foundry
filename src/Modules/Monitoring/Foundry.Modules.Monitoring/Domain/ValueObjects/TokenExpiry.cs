using System.Diagnostics;

using Foundry.Modules.Monitoring.Contracts;

namespace Foundry.Modules.Monitoring.Domain.ValueObjects;

/// <summary>
/// The account token's <em>Token Expiry</em>, as last read from the provider when the token was set.
/// </summary>
public abstract record TokenExpiry
{
    /// <summary>How far ahead of the expiry an operator is warned.</summary>
    public static readonly TimeSpan WarningWindow = AccountTokenExpiryNotification.WarningWindow;

    private TokenExpiry() { }

    /// <summary>Foundry has not read the expiry.</summary>
    public sealed record NotChecked : TokenExpiry;

    /// <summary>The provider reported no expiry for the token.</summary>
    public sealed record Unavailable : TokenExpiry;

    /// <summary>The provider says the token never expires.</summary>
    public sealed record Never : TokenExpiry;

    /// <summary>The token expires at <paramref name="ExpiresAt"/>.</summary>
    public sealed record On(DateTimeOffset ExpiresAt) : TokenExpiry;

    /// <summary>The expiry's status at <paramref name="now"/>.</summary>
    public TokenExpiryStatus StatusAt(DateTimeOffset now) => this switch
    {
        On on when now >= on.ExpiresAt => TokenExpiryStatus.Expired,
        On on when on.ExpiresAt - now <= WarningWindow => TokenExpiryStatus.ExpiringSoon,
        On => TokenExpiryStatus.Current,
        Never => TokenExpiryStatus.Never,
        Unavailable => TokenExpiryStatus.Unavailable,
        NotChecked => TokenExpiryStatus.NotChecked,
        _ => throw new UnreachableException($"Unhandled TokenExpiry variant: {GetType().Name}"),
    };
}
