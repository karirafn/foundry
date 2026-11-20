using Foundry.Modules.Monitoring.Contracts;

namespace Foundry.Modules.Issues.Contracts;

/// <summary>The issues whose live worker or Publisher keeps their repository from being removed.</summary>
public interface IRepositoryRemovalBlockers
{
    /// <summary>
    /// Every issue of <paramref name="repositoryIds"/> in an in-flight state (<c>triage_in_progress</c>,
    /// <c>in_progress</c>, <c>revision_in_progress</c> or <c>review_in_progress</c>), except those whose run waits for a
    /// publish retry: that run holds no live process and is discarded with the repository. An in-flight issue whose run
    /// is not created yet blocks.
    /// </summary>
    Task<IReadOnlyList<RemovalBlockingIssue>> GetBlockingIssuesAsync(
        IReadOnlyCollection<MonitoredRepositoryId> repositoryIds,
        CancellationToken cancellationToken);
}

public sealed record RemovalBlockingIssue(MonitoredRepositoryId RepositoryId, int IssueNumber, string State);
