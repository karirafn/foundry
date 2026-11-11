using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Contracts.Queries;

/// <summary>Reads an issue's content from its provider with the repository's account token.</summary>
public interface IIssueContentQueries
{
    /// <summary>The issue's title, body and comments, read live from the provider.</summary>
    Task<Result<IssueContent>> GetIssueContentAsync(
        MonitoredRepositoryId repositoryId,
        int issueNumber,
        CancellationToken cancellationToken);
}
