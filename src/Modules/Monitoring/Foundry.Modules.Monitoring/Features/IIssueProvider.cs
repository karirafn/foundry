using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Features;

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
