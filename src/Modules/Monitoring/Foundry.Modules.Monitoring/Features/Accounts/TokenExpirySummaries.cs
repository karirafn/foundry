using System.Diagnostics;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.ValueObjects;

namespace Foundry.Modules.Monitoring.Features.Accounts;

/// <summary>Maps a <see cref="TokenExpiry"/> to its wire summary, judged at a point in time.</summary>
internal static class TokenExpirySummaries
{
    internal const string NotChecked = "notChecked";
    internal const string Unavailable = "unavailable";
    internal const string Never = "never";
    internal const string Current = "current";
    internal const string ExpiringSoon = "expiringSoon";
    internal const string Expired = "expired";

    internal static TokenExpirySummary From(TokenExpiry expiry, DateTimeOffset now)
    {
        TokenExpiryStatus status = expiry.StatusAt(now);
        string wireStatus = status switch
        {
            TokenExpiryStatus.NotChecked => NotChecked,
            TokenExpiryStatus.Unavailable => Unavailable,
            TokenExpiryStatus.Never => Never,
            TokenExpiryStatus.Current => Current,
            TokenExpiryStatus.ExpiringSoon => ExpiringSoon,
            TokenExpiryStatus.Expired => Expired,
            _ => throw new UnreachableException($"Unhandled TokenExpiryStatus: {status}"),
        };

        return new TokenExpirySummary(wireStatus, expiry is TokenExpiry.On on ? on.ExpiresAt : null);
    }
}
