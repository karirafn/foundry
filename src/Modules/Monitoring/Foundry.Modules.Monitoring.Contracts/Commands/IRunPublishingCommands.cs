using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Contracts.Commands;

/// <summary>Provider writes and reads Foundry performs when it publishes a worker run (ADR 0100).</summary>
public interface IRunPublishingCommands
{
    /// <summary>
    /// Opens a pull/merge request from <paramref name="branchName"/> into the repository's default branch with the
    /// account PAT and returns its web URL.
    /// </summary>
    Task<Result<string>> OpenPullRequestAsync(
        MonitoredRepositoryId repositoryId,
        string branchName,
        string title,
        string body,
        CancellationToken cancellationToken);

    /// <summary>
    /// Posts <paramref name="body"/> as one comment on the pull/merge request at <paramref name="pullRequestUrl"/> and
    /// records it in the posted-comment registry, so no comment reader treats it as feedback.
    /// </summary>
    Task<Result> PostRunSummaryAsync(
        MonitoredRepositoryId repositoryId,
        string pullRequestUrl,
        string body,
        CancellationToken cancellationToken);

    /// <summary>The account identity the Publisher rewrites a run's commits to.</summary>
    Task<Result<CommitIdentity>> GetCommitIdentityAsync(
        MonitoredRepositoryId repositoryId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Whether <paramref name="ancestorRef"/> (a branch or commit) is an ancestor of, or equal to, the head of
    /// <paramref name="branchName"/>.
    /// </summary>
    Task<Result<bool>> IsAncestorOfBranchAsync(
        MonitoredRepositoryId repositoryId,
        string ancestorRef,
        string branchName,
        CancellationToken cancellationToken);
}

/// <summary>The author and committer identity of the account Foundry publishes as.</summary>
public sealed record CommitIdentity(string Name, string Email);
