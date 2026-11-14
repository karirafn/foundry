namespace Foundry.Modules.Monitoring.Contracts.Queries;

public interface IAccountTokenHealthQueries
{
    /// <summary>Counts the accounts whose token the provider has refused (an Invalid Token).</summary>
    Task<int> CountInvalidAsync(CancellationToken cancellationToken);
}
