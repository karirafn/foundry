using Foundry.WebApi.Modules.Monitoring.Domain;
using Foundry.WebApi.Shared.Abstractions;

namespace Foundry.WebApi.Modules.Monitoring.Features;

public interface IIssueProvider
{
    Task<Result<IReadOnlyList<ProviderIssue>>> GetIssuesAsync(
        RepositorySlug slug,
        CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<int>>> GetDependenciesAsync(
        RepositorySlug slug,
        int issueNumber,
        CancellationToken cancellationToken);
}
