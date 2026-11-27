using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;

namespace Foundry.Modules.Workers.Domain.ValueObjects;

/// <summary>What the Publisher's Publish mode reported for a run (ADR 0100).</summary>
public abstract record PublishResult
{
    private PublishResult()
    {
    }

    /// <summary>The run's commits are on its branch, whose head is now <paramref name="Head"/>.</summary>
    /// <param name="BranchCommitCount">The commits the branch holds beyond the default branch.</param>
    /// <param name="CommitSubjects">The subjects of the run's commits as pushed, oldest first.</param>
    /// <param name="RevertedFiles">The files the Diff gate reverted before the push (#788).</param>
    /// <param name="GenericFindings">
    /// The Diff gate's generic findings, pushed with the run for review in its pull request (#956, ADR 0107).
    /// </param>
    public sealed record Published(
        CommitSha Head,
        int BranchCommitCount,
        IReadOnlyList<string> CommitSubjects,
        IReadOnlyList<RevertedFile> RevertedFiles,
        IReadOnlyList<SecretFinding> GenericFindings) : PublishResult;

    /// <summary>The run produced no commits: no Run bundle, or an empty one.</summary>
    public sealed record NothingToPublish : PublishResult;

    /// <summary>
    /// The run's work went to <paramref name="SideBranch"/> instead of <paramref name="Branch"/>, for
    /// <paramref name="Reason"/>: someone pushed to the branch during the run and the work could not join it, or the
    /// provider refused the lease push of a rebased tip (ADR 0101).
    /// </summary>
    /// <param name="RevertedFiles">The files the Diff gate reverted before the push (#788).</param>
    /// <param name="GenericFindings">
    /// The Diff gate's generic findings, pushed with the run for review in its pull request (#956, ADR 0107).
    /// </param>
    public sealed record Diverged(
        BranchName Branch,
        BranchName SideBranch,
        CommitSha Head,
        BranchDivergenceReason Reason,
        IReadOnlyList<RevertedFile> RevertedFiles,
        IReadOnlyList<SecretFinding> GenericFindings) : PublishResult;

    /// <summary>
    /// The Diff gate found possible secrets of a provider pattern in the Run commits, so nothing was pushed (#788,
    /// #956). The run keeps its Hand-off volume until it is discarded; there is no way to publish it anyway.
    /// </summary>
    public sealed record SecretDetected(IReadOnlyList<SecretFinding> Findings) : PublishResult;

    /// <summary>
    /// Publishing failed. A transient failure (network, provider outage) is retried with backoff; a permanent one (an
    /// invalid bundle, a token the provider refused) is not. An unreachable provider is transient and also pauses its
    /// host.
    /// </summary>
    public sealed record Failed(string Reason, PublishFailureKind Kind) : PublishResult
    {
        /// <summary>Whether a retry may succeed, so the failure is retried with backoff.</summary>
        public bool Transient => Kind is PublishFailureKind.Transient or PublishFailureKind.Unreachable;

        /// <summary>Whether the provider refused the account token, so the run's repository is probed again.</summary>
        public bool Refused => Kind is PublishFailureKind.Refused;

        /// <summary>Whether the provider could not be reached, which pauses the run's provider host.</summary>
        public bool Unreachable => Kind is PublishFailureKind.Unreachable;
    }
}
