namespace Foundry.Modules.Workers.Contracts.Queries;

/// <summary>Answers whether a branch belongs to a running worker — the push-webhook relevance filter.</summary>
public interface IRunningBranchQueries
{
    /// <summary>
    /// Whether <paramref name="branchName"/> of the monitored repository is the branch of a running worker
    /// run (an <c>ActiveRun</c>). A run not yet recorded as running does not count.
    /// </summary>
    Task<bool> IsRunningBranchAsync(
        Guid monitoredRepositoryId,
        string branchName,
        CancellationToken cancellationToken);
}
