using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Shared;

using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Monitoring.Features;

internal sealed class NamespaceDeriver(
    GitHubHttpClient gitHubHttpClient,
    GitLabHttpClient gitLabHttpClient,
    ILogger<NamespaceDeriver> logger) : INamespaceDeriver
{
    public async Task<NamespaceDerivationOutcome> DeriveAsync(
        Credential credential,
        CancellationToken cancellationToken)
    {
        if (credential.Token is null)
        {
            return new NamespaceDerivationOutcome.Unavailable();
        }

        try
        {
            bool isGitLab = credential is GitLabCredential;
            Result<IReadOnlyList<ProviderRepository>> listResult = isGitLab
                ? await gitLabHttpClient.ListRepositoriesAsync(
                    credential.ApiBaseUrl,
                    credential.Token,
                    cancellationToken)
                : await gitHubHttpClient.ListRepositoriesAsync(
                    credential.ApiBaseUrl,
                    credential.Token,
                    cancellationToken);

            if (listResult is not Result<IReadOnlyList<ProviderRepository>>.Success listSuccess)
            {
                return new NamespaceDerivationOutcome.Unavailable();
            }

            return new NamespaceDerivationOutcome.Derived(
                NamespaceDerivation.FromWritableRepositories(listSuccess.Value));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(
                ex,
                "Namespace derivation failed for credential {CredentialId}; treating as unavailable.",
                credential.Id);
            return new NamespaceDerivationOutcome.Unavailable();
        }
    }
}
