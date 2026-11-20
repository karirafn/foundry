using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Contracts.Queries;

/// <summary>Reads an issue's content, and its pull request's description, from its provider with the repository's account token.</summary>
public interface IIssueContentQueries
{
    /// <summary>The issue's title, body and comments, read live from the provider.</summary>
    Task<Result<IssueContent>> GetIssueContentAsync(
        MonitoredRepositoryId repositoryId,
        int issueNumber,
        CancellationToken cancellationToken);

    /// <summary>
    /// The description of the pull/merge request at <paramref name="pullRequestUrl"/>, read live from the provider,
    /// so a person's edits are included. A pull request without a description reads as empty.
    /// </summary>
    Task<Result<string>> GetPullRequestDescriptionAsync(
        MonitoredRepositoryId repositoryId,
        string pullRequestUrl,
        CancellationToken cancellationToken);
}
