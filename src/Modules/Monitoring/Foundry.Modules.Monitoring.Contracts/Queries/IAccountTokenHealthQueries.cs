namespace Foundry.Modules.Monitoring.Contracts.Queries;

public interface IAccountTokenHealthQueries
{
    /// <summary>Counts the accounts whose token the provider has refused (an Invalid Token).</summary>
    Task<int> CountInvalidAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Counts the Valid accounts whose token is expiring soon or has expired at <paramref name="now"/>.
    /// Invalid accounts are not counted: the account-token banner already covers them.
    /// </summary>
    Task<AccountTokenExpiryCounts> CountExpiryAsync(DateTimeOffset now, CancellationToken cancellationToken);
}

/// <summary>How many Valid accounts have a token expiring soon, and how many have an expired one.</summary>
public sealed record AccountTokenExpiryCounts(int ExpiringSoon, int Expired)
{
    public bool Any => ExpiringSoon + Expired > 0;
}
