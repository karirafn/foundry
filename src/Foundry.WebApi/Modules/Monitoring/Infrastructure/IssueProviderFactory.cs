using Foundry.WebApi.Modules.Monitoring.Domain;
using Foundry.WebApi.Modules.Monitoring.Features;

namespace Foundry.WebApi.Modules.Monitoring.Infrastructure;

internal sealed class IssueProviderFactory(GitHubHttpClient gitHubHttpClient) : IIssueProviderFactory
{
    public IIssueProvider CreateProvider(Account account, string token)
    {
        return account switch
        {
            GitHubAccount => new GitHubIssueProvider(gitHubHttpClient, token),
            _ => throw new NotSupportedException(
                $"No issue provider is registered for account type '{account.GetType().Name}'."),
        };
    }
}
