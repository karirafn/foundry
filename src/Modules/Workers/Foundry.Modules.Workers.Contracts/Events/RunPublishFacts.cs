namespace Foundry.Modules.Workers.Contracts;

/// <summary>
/// What publishing a worker run did (ADR 0100): the head Foundry pushed to the run's branch, the pull request it opened
/// or found, and the side branch it pushed to instead when the run's branch diverged.
/// </summary>
/// <param name="PushedHeadSha">The branch head after the push, or <see langword="null"/> when nothing was pushed.</param>
/// <param name="PullRequestUrl">The open or merged pull request for the branch, or <see langword="null"/> when there is none.</param>
/// <param name="DivergedSideBranch">
/// The side branch holding the run's work when merging it into a branch someone pushed to during the run conflicted;
/// <see langword="null"/> otherwise.
/// </param>
/// <param name="BranchCommitCount">The commits the run's branch holds beyond the default branch after publishing.</param>
public sealed record RunPublishFacts(
    string? PushedHeadSha,
    string? PullRequestUrl,
    string? DivergedSideBranch,
    int BranchCommitCount);
