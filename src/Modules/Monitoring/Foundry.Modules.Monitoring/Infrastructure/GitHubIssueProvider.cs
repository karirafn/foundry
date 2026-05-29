using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Features;
using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Infrastructure;

internal sealed class GitHubIssueProvider(GitHubHttpClient httpClient, string token, Uri baseUrl) : IIssueProvider
{
    public Task<Result<IReadOnlyList<ProviderIssue>>> GetIssuesAsync(
        RepositorySlug slug,
        CancellationToken cancellationToken)
    {
        return httpClient.GetIssuesAsync(baseUrl, slug, token, cancellationToken);
    }

    public Task<Result<IReadOnlyList<int>>> GetDependenciesAsync(
        RepositorySlug slug,
        int issueNumber,
        CancellationToken cancellationToken)
    {
        return httpClient.GetDependenciesAsync(baseUrl, slug, issueNumber, token, cancellationToken);
    }
}
