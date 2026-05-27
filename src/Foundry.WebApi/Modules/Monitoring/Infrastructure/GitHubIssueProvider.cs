using Foundry.WebApi.Modules.Monitoring.Domain;
using Foundry.WebApi.Modules.Monitoring.Features;
using Foundry.WebApi.Shared.Abstractions;

namespace Foundry.WebApi.Modules.Monitoring.Infrastructure;

internal sealed class GitHubIssueProvider(GitHubHttpClient httpClient, string token) : IIssueProvider
{
    public Task<Result<IReadOnlyList<ProviderIssue>>> GetIssuesAsync(
        RepositorySlug slug,
        CancellationToken cancellationToken)
    {
        return httpClient.GetIssuesAsync(slug, token, cancellationToken);
    }
}
