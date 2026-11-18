namespace Foundry.Modules.Monitoring.Contracts;

/// <summary>
/// The system notification that warns the operator how many Valid accounts have a token expiring soon or
/// already expired. The server builds all of its copy, so the dashboard shows the message as given.
/// </summary>
public static class AccountTokenExpiryNotification
{
    public const string Category = "account-token-expiry";

    /// <summary>
    /// How far ahead of a token's expiry the operator is warned. The one definition of the window: the
    /// Monitoring module's <c>TokenExpiry</c> reads it from here, so the copy and the status agree.
    /// </summary>
    public static readonly TimeSpan WarningWindow = TimeSpan.FromDays(14);

    /// <summary>
    /// The banner copy for <paramref name="expiringSoon"/> accounts whose token expires within the
    /// <see cref="WarningWindow"/> and <paramref name="expired"/> accounts whose token has expired; empty
    /// when there are none.
    /// </summary>
    public static string Message(int expiringSoon, int expired)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(expiringSoon);
        ArgumentOutOfRangeException.ThrowIfNegative(expired);

        if (expiringSoon + expired == 0)
        {
            return string.Empty;
        }

        int days = WarningWindow.Days;
        string tail = expiringSoon + expired == 1
            ? "enter a new one before monitoring stops."
            : "enter new ones before monitoring stops.";

        string clause = (expiringSoon, expired) switch
        {
            (1, 0) => $"1 account has a token expiring within {days} days",
            (_, 0) => $"{expiringSoon} accounts have tokens expiring within {days} days",
            (0, 1) => "1 account has an expired token",
            (0, _) => $"{expired} accounts have expired tokens",
            _ => $"{ExpiredLead(expired)} and {ExpiringSoonFollow(expiringSoon, days)}",
        };

        return $"{clause} — {tail}";
    }

    private static string ExpiredLead(int expired) =>
        expired == 1 ? "1 account has an expired token" : $"{expired} accounts have expired tokens";

    private static string ExpiringSoonFollow(int expiringSoon, int days) =>
        expiringSoon == 1
            ? $"1 has a token expiring within {days} days"
            : $"{expiringSoon} have tokens expiring within {days} days";
}
