using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Contracts.Commands;

/// <summary>
/// Operator-initiated writes against a monitored repository's work branches.
/// </summary>
public interface IWorkBranchCommands
{
    /// <summary>
    /// Deletes <paramref name="branchName"/> on the provider so the next dispatch re-creates it
    /// from the default branch. Returns <c>true</c> when the branch was deleted and <c>false</c>
    /// when it was already absent. Never deletes the repository's default branch.
    /// </summary>
    Task<Result<bool>> DeleteBranchAsync(
        MonitoredRepositoryId repositoryId,
        string branchName,
        CancellationToken cancellationToken);
}
