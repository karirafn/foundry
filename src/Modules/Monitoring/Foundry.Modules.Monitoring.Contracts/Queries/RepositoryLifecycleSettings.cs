namespace Foundry.Modules.Monitoring.Contracts.Queries;

/// <summary>
/// The repository settings that turn lifecycle steps on or off: the rebase after a run, the review of the pull request,
/// and the merge once its checks pass.
/// </summary>
public sealed record RepositoryLifecycleSettings(bool RebaseAfterRun, bool ReviewPullRequests, bool MergeWhenChecksPass)
{
    /// <summary>A new repository's settings, read for a repository the settings read does not find.</summary>
    public static RepositoryLifecycleSettings Default { get; } = new(
        RebaseAfterRun: true,
        ReviewPullRequests: true,
        MergeWhenChecksPass: false);
}
