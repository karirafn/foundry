using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Features;

namespace Foundry.Modules.Monitoring.Infrastructure;

internal sealed class IssueProviderFactory(GitHubHttpClient gitHubHttpClient) : IIssueProviderFactory
{
    public IIssueProvider CreateProvider(Account account, string token)
    {
        return account switch
        {
            GitHubAccount gitHub => new GitHubIssueProvider(gitHubHttpClient, token, gitHub.BaseUrl),
            _ => throw new NotSupportedException(
                $"No issue provider is registered for account type '{account.GetType().Name}'."),
        };
    }
}
