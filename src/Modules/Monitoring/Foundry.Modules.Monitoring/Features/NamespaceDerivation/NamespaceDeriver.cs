using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Modules.Monitoring.Infrastructure.GitHub;
using Foundry.Modules.Monitoring.Infrastructure.GitLab;
using Foundry.Shared;

using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Monitoring.Features.NamespaceDerivation;

internal sealed class NamespaceDeriver(
    GitHubHttpClient gitHubHttpClient,
    GitLabHttpClient gitLabHttpClient,
    ILogger<NamespaceDeriver> logger) : INamespaceDeriver
{
    public Task<NamespaceDerivationOutcome> DeriveAsync(
        Credential credential,
        CancellationToken cancellationToken)
    {
        if (credential.Token is null)
        {
            return Task.FromResult<NamespaceDerivationOutcome>(new NamespaceDerivationOutcome.Unavailable());
        }

        return DeriveAsync(
            credential.ApiBaseUrl,
            credential.Token,
            credential is GitLabCredential,
            cancellationToken);
    }

    public async Task<NamespaceDerivationOutcome> DeriveAsync(
        Uri apiBaseUrl,
        string token,
        bool isGitLab,
        CancellationToken cancellationToken)
    {
        try
        {
            Result<IReadOnlyList<ProviderRepository>> listResult = isGitLab
                ? await gitLabHttpClient.ListRepositoriesAsync(
                    apiBaseUrl,
                    token,
                    cancellationToken)
                : await gitHubHttpClient.ListRepositoriesAsync(
                    apiBaseUrl,
                    token,
                    cancellationToken);

            if (listResult is not Result<IReadOnlyList<ProviderRepository>>.Success listSuccess)
            {
                return new NamespaceDerivationOutcome.Unavailable();
            }

            IReadOnlyList<ProviderRepository> allRepos = listSuccess.Value;
            IReadOnlyList<ProviderRepository> writableRepos = allRepos
                .Where(r => r.CanPush)
                .ToList();
            return new NamespaceDerivationOutcome.Derived(
                NamespaceDerivation.FromWritableRepositories(allRepos),
                writableRepos);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(
                ex,
                "Namespace derivation failed for base URL {ApiBaseUrl}; treating as unavailable.",
                apiBaseUrl);
            return new NamespaceDerivationOutcome.Unavailable();
        }
    }
}
