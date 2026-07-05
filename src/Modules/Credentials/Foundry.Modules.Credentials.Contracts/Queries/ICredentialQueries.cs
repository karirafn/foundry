namespace Foundry.Modules.Credentials.Contracts.Queries;

public interface ICredentialQueries
{
    Task<string?> GetAuthModeAsync(CancellationToken cancellationToken);

    Task<(string Key, string Value)?> GetAuthEnvironmentVariableAsync(CancellationToken cancellationToken);

    Task<bool> IsValidAsync(CancellationToken cancellationToken);

    Task<ClaudeAccountSummary?> GetSummaryAsync(CancellationToken cancellationToken);
}
