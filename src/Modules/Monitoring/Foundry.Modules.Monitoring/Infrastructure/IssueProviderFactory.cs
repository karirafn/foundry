using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Modules.Monitoring.Infrastructure.GitHub;
using Foundry.Modules.Monitoring.Infrastructure.GitLab;

namespace Foundry.Modules.Monitoring.Infrastructure;

internal sealed class IssueProviderFactory(
    GitHubHttpClient gitHubHttpClient,
    GitLabHttpClient gitLabHttpClient) : IIssueProviderFactory
{
    public IIssueProvider CreateProvider(Credential credential, string token)
    {
        return credential switch
        {
            GitHubCredential gitHub => new GitHubIssueProvider(gitHubHttpClient, token, gitHub.ApiBaseUrl),
            GitLabCredential gitLab => new GitLabIssueProvider(gitLabHttpClient, token, gitLab.ApiBaseUrl),
            _ => throw new NotSupportedException(
                $"No issue provider is registered for credential type '{credential.GetType().Name}'."),
        };
    }
}
