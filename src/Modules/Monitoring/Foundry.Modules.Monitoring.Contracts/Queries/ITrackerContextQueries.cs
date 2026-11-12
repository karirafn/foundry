namespace Foundry.Modules.Monitoring.Contracts.Queries;

/// <summary>Reads the issues around a dispatched issue from its provider with the repository's account token.</summary>
public interface ITrackerContextQueries
{
    /// <summary>
    /// The Tracker Context for an issue, read live from the provider. Never fails: every part that could not be
    /// read is named in the returned context, so the dispatch always goes ahead.
    /// </summary>
    Task<TrackerContext> GetTrackerContextAsync(
        MonitoredRepositoryId repositoryId,
        int issueNumber,
        IssueContent issueContent,
        CancellationToken cancellationToken);
}
