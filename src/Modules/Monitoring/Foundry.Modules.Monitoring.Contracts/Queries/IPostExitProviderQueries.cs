using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Contracts.Queries;

public interface IPostExitProviderQueries
{
    Task<Result<bool>> CreateBranchAsync(
        MonitoredRepositoryId repositoryId,
        string branchName,
        CancellationToken cancellationToken);

    Task<Result<bool>> HasBranchCommitsAsync(
        MonitoredRepositoryId repositoryId,
        string branchName,
        CancellationToken cancellationToken);

    Task<Result<string>> GetPullRequestByBranchAsync(
        MonitoredRepositoryId repositoryId,
        string branchName,
        CancellationToken cancellationToken);

    Task<Result<MergeRequestByBranch>> GetMergeRequestByBranchAsync(
        MonitoredRepositoryId repositoryId,
        string branchName,
        CancellationToken cancellationToken);

    Task<Result<LatestBranchCommit>> GetLatestBranchCommitAsync(
        MonitoredRepositoryId repositoryId,
        string branchName,
        CancellationToken cancellationToken);
}
