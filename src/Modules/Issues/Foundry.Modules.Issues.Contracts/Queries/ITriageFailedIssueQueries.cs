using Foundry.Modules.Monitoring.Contracts;

namespace Foundry.Modules.Issues.Contracts;

/// <summary>The failed triages Monitoring watches for an issue edit (issue #773).</summary>
public interface ITriageFailedIssueQueries
{
    /// <summary>The numbers of the repository's <c>triage_failed</c> issues.</summary>
    Task<IReadOnlySet<int>> GetTriageFailedIssueNumbersAsync(
        MonitoredRepositoryId repositoryId,
        CancellationToken cancellationToken);
}
