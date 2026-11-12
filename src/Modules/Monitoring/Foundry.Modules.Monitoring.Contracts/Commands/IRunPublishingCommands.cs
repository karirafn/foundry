using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Contracts.Commands;

/// <summary>
/// Provider writes and reads Foundry performs when it publishes a worker run (ADR 0100). Every call uses the
/// account PAT resolved by repository at call time, never a token carried by the caller. Failures carry
/// <see cref="RunPublishingErrors.TransientCode"/> (may succeed on retry),
/// <see cref="RunPublishingErrors.PermanentCode"/> (will not), or, from
/// <see cref="IsAncestorOfBranchAsync"/>, <see cref="RunPublishingErrors.RefNotFoundCode"/>. No call retries.
/// </summary>
/// <remarks>
/// The clone URL and account PAT the Publisher needs at publish time come from
/// <see cref="IRepositoryDispatchQueries.GetDispatchInfoAsync"/>, the same resolution dispatch uses.
/// </remarks>
public interface IRunPublishingCommands
{
    /// <summary>
    /// Opens a pull/merge request from <paramref name="branchName"/> into the repository's default branch and
    /// returns its web URL. When the provider reports one already open for the branch, returns that one's URL,
    /// so a retried publish never fails on its own earlier success.
    /// </summary>
    Task<Result<string>> OpenPullRequestAsync(
        MonitoredRepositoryId repositoryId,
        string branchName,
        string title,
        string body,
        CancellationToken cancellationToken);

    /// <summary>
    /// Posts <paramref name="body"/> as one comment on the pull/merge request at
    /// <paramref name="pullRequestUrl"/> and saves its ID in the posted-comment registry in the same call,
    /// so no comment reader treats it as review feedback.
    /// </summary>
    /// <remarks>The registry save flushes the scope's unit of work, so changes the caller has staged on the
    /// same scope are saved with it.</remarks>
    Task<Result> PostRunSummaryAsync(
        MonitoredRepositoryId repositoryId,
        string pullRequestUrl,
        string body,
        CancellationToken cancellationToken);

    /// <summary>
    /// The account identity the Publisher rewrites run commits to. GitHub: the user's name (else login) and
    /// the account's noreply address <c>&lt;id&gt;+&lt;login&gt;@users.noreply.&lt;host&gt;</c>. GitLab: the
    /// user's name (else username) and the commit email when set, else the public email, else the private
    /// commit address <c>&lt;id&gt;-&lt;username&gt;@users.noreply.&lt;host&gt;</c>.
    /// </summary>
    Task<Result<CommitIdentity>> GetCommitIdentityAsync(
        MonitoredRepositoryId repositoryId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Whether the commit or branch <paramref name="ancestorRef"/> is an ancestor of, or equal to, the head of
    /// <paramref name="branchName"/>. Fails with <see cref="RunPublishingErrors.RefNotFoundCode"/>
    /// (<see cref="ErrorKind.NotFound"/>) when either ref is missing.
    /// </summary>
    Task<Result<bool>> IsAncestorOfBranchAsync(
        MonitoredRepositoryId repositoryId,
        string ancestorRef,
        string branchName,
        CancellationToken cancellationToken);
}

/// <summary>The author and committer identity of a published run commit.</summary>
public sealed record CommitIdentity(string Name, string Email);
