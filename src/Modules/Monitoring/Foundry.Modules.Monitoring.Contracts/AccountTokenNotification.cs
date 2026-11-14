namespace Foundry.Modules.Monitoring.Contracts;

/// <summary>
/// The system notification that tells the operator how many accounts have an Invalid Token. The server
/// builds all of its copy, so the dashboard shows the message as given.
/// </summary>
public static class AccountTokenNotification
{
    public const string Category = "account-token";

    /// <summary>
    /// The banner copy for <paramref name="count"/> accounts with an invalid token; empty when there are none.
    /// </summary>
    public static string Message(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        return count switch
        {
            0 => string.Empty,
            1 => "1 account has an invalid token — enter a new one to resume monitoring.",
            _ => $"{count} accounts have invalid tokens — enter new ones to resume monitoring.",
        };
    }
}
