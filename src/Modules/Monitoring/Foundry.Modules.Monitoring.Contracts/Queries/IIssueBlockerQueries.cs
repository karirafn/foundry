using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Contracts.Queries;

/// <summary>
/// Reads an issue's open same-repository blockers live from the provider.
/// </summary>
/// <remarks>
/// Used at claim time so an issue that gained a blocker after the last dependency pass is never
/// dispatched. The provider boundary rules are those of the poll-path dependency read: closed
/// blockers and cross-repository links are filtered, links with an undeterminable project are kept
/// (fail-safe), and a GitLab 403/404 on the Issue Links API degrades to no blockers.
/// </remarks>
public interface IIssueBlockerQueries
{
    /// <summary>
    /// Returns the issue numbers of the open same-repository issues blocking
    /// <paramref name="issueNumber"/>, or a failure when the repository, its credential, or the
    /// provider call cannot be resolved.
    /// </summary>
    Task<Result<IReadOnlyList<int>>> GetOpenBlockersAsync(
        MonitoredRepositoryId repositoryId,
        int issueNumber,
        CancellationToken cancellationToken);
}
