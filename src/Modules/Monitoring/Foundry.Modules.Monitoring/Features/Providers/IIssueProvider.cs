using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Commands;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers.BlockerLinks;
using Foundry.Modules.Monitoring.Features.Providers.Checks;
using Foundry.Modules.Monitoring.Features.Providers.Comments;
using ProviderChecks = Foundry.Modules.Monitoring.Features.Providers.Checks.Checks;
using Foundry.Modules.Monitoring.Features.Providers.Feedback;
using Foundry.Modules.Monitoring.Features.Providers.Merge;
using Foundry.Modules.Monitoring.Features.Providers.Rebase;
using Foundry.Modules.Monitoring.Features.Providers.RootDirectory;
using Foundry.Modules.Monitoring.Features.Providers.TrackerReads;
using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Features.Providers;

internal interface IIssueProvider
{
    Task<Result<IssueListing>> GetIssuesAsync(
        RepositorySlug slug,
        CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<int>>> GetDependenciesAsync(
        RepositorySlug slug,
        int issueNumber,
        CancellationToken cancellationToken);

    Task<Result<bool>> IsIssueClosedAsync(
        RepositorySlug slug,
        int issueNumber,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads whether the issue is closed together with its body, in a single request, for the awaiting-input step,
    /// which compares the body against its Issue Watch. Callers that need only the state keep
    /// <see cref="IsIssueClosedAsync"/>, which downloads no body.
    /// </summary>
    /// <remarks>Providers without a status read keep this default, which fails with
    /// <see cref="TrackerReadErrors.NotSupportedCode"/> without any provider call.</remarks>
    Task<Result<IssueStatus>> GetIssueStatusAsync(
        RepositorySlug slug,
        int issueNumber,
        CancellationToken cancellationToken)
        => Task.FromResult(Result<IssueStatus>.Fail(TrackerReadErrors.NotSupported()));

    /// <summary>
    /// Reads where <paramref name="branchName"/> points: <see cref="BranchHead.Present"/> with its head commit, or
    /// <see cref="BranchHead.Absent"/> when the provider reports the branch missing (404). Any other non-success
    /// response is a failed read.
    /// </summary>
    /// <remarks>Providers without a branch-head read keep this default, which fails with
    /// <see cref="TrackerReadErrors.NotSupportedCode"/> without any provider call.</remarks>
    Task<Result<BranchHead>> GetBranchHeadAsync(
        RepositorySlug slug,
        string branchName,
        CancellationToken cancellationToken)
        => Task.FromResult(Result<BranchHead>.Fail(TrackerReadErrors.NotSupported()));

    Task<Result<PullRequestStatus>> GetPullRequestStatusAsync(
        RepositorySlug slug,
        string pullRequestUrl,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads the description of the pull/merge request at <paramref name="pullRequestUrl"/>, live: GitHub's
    /// <c>body</c>, GitLab's <c>description</c>. A pull request without one reads as empty.
    /// </summary>
    /// <remarks>Providers without a description read keep this default, which fails with
    /// <see cref="TrackerReadErrors.NotSupportedCode"/> without any provider call.</remarks>
    Task<Result<string>> GetPullRequestBodyAsync(
        RepositorySlug slug,
        string pullRequestUrl,
        CancellationToken cancellationToken)
        => Task.FromResult(Result<string>.Fail(TrackerReadErrors.NotSupported()));

    /// <summary>
    /// Reads the actionable feedback on a pull/merge request thread (see <see cref="ActionableFeedbackPolicy"/>),
    /// never a comment in <paramref name="postedComments"/>.
    /// </summary>
    Task<Result<ReviewFeedback>> GetReviewFeedbackAsync(
        RepositorySlug slug,
        string pullRequestUrl,
        DateTimeOffset since,
        PostedCommentIds postedComments,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads an issue's own comment thread: every comment created after <paramref name="since"/>, oldest
    /// first, except those in <paramref name="postedComments"/>. Bot and system comments are kept and
    /// flagged so callers decide; pull/merge request threads are read with <see cref="GetReviewFeedbackAsync"/>.
    /// </summary>
    /// <remarks>Providers without an issue-thread reader keep this default, which fails with
    /// <see cref="IssueFeedbackErrors.NotSupportedCode"/> without any provider call.</remarks>
    Task<Result<IReadOnlyList<ProviderComment>>> GetIssueFeedbackAsync(
        RepositorySlug slug,
        int issueNumber,
        DateTimeOffset since,
        PostedCommentIds postedComments,
        CancellationToken cancellationToken)
        => Task.FromResult(Result<IReadOnlyList<ProviderComment>>.Fail(IssueFeedbackErrors.NotSupported()));

    /// <summary>
    /// Reads an issue's title, body and every comment on its thread (paged, oldest first), whoever wrote them,
    /// leaving out provider system notes. Restricted worker runs get the issue from this read rather than from
    /// the provider (ADR 0098).
    /// </summary>
    /// <remarks>Providers without an issue-content reader keep this default, which fails with
    /// <see cref="IssueContentErrors.NotSupportedCode"/> without any provider call.</remarks>
    Task<Result<ProviderIssueContent>> GetIssueContentAsync(
        RepositorySlug slug,
        int issueNumber,
        CancellationToken cancellationToken)
        => Task.FromResult(Result<ProviderIssueContent>.Fail(IssueContentErrors.NotSupported()));

    /// <summary>
    /// Reads the same-project issues linked to <paramref name="issueNumber"/>, in any state, with each link's
    /// relation from the requested issue's side.
    /// </summary>
    /// <remarks>Providers without a link reader keep this default, which fails with
    /// <see cref="TrackerReadErrors.NotSupportedCode"/> without any provider call.</remarks>
    Task<Result<IReadOnlyList<IssueReference>>> GetIssueLinksAsync(
        RepositorySlug slug,
        int issueNumber,
        CancellationToken cancellationToken)
        => Task.FromResult(Result<IReadOnlyList<IssueReference>>.Fail(TrackerReadErrors.NotSupported()));

    /// <summary>
    /// Reads one issue's title, state, labels, closing merge-request state and body.
    /// </summary>
    /// <remarks>Providers without a referenced-issue reader keep this default, which fails with
    /// <see cref="TrackerReadErrors.NotSupportedCode"/> without any provider call.</remarks>
    Task<Result<ProviderReferencedIssue>> GetReferencedIssueAsync(
        RepositorySlug slug,
        int issueNumber,
        CancellationToken cancellationToken)
        => Task.FromResult(Result<ProviderReferencedIssue>.Fail(TrackerReadErrors.NotSupported()));

    /// <summary>
    /// Reads the repository's open issues, newest first, at most 100, never a pull/merge request.
    /// </summary>
    /// <remarks>Providers without an open-issue reader keep this default, which fails with
    /// <see cref="TrackerReadErrors.NotSupportedCode"/> without any provider call.</remarks>
    Task<Result<IReadOnlyList<OpenIssue>>> GetOpenIssuesAsync(
        RepositorySlug slug,
        CancellationToken cancellationToken)
        => Task.FromResult(Result<IReadOnlyList<OpenIssue>>.Fail(TrackerReadErrors.NotSupported()));

    /// <summary>
    /// Posts <paramref name="body"/> to <paramref name="thread"/> with the account PAT and returns the
    /// provider's ID of the new comment, which the caller records with <see cref="PostedCommentRegistry"/>
    /// so no comment reader treats it as feedback. A failure is <see cref="CommentPostErrors.TransientCode"/>
    /// (5xx, timeout, rate limit) or <see cref="CommentPostErrors.PermanentCode"/> (any other 4xx, an invalid
    /// request); this call never retries.
    /// </summary>
    /// <remarks>Providers without comment posting keep this default, which fails permanently without any
    /// provider call.</remarks>
    Task<Result<ProviderCommentId>> PostCommentAsync(
        RepositorySlug slug,
        CommentThread thread,
        string body,
        CancellationToken cancellationToken)
        => Task.FromResult(Result<ProviderCommentId>.Fail(CommentPostErrors.NotSupported()));

    /// <summary>
    /// Posts <paramref name="comments"/> as inline review comments on the pull/merge request at
    /// <paramref name="pullRequestUrl"/>, on lines of <paramref name="commitSha"/>'s diff, and returns the registry key
    /// of each comment created. GitHub posts them in one review with <paramref name="summary"/> as its body and fails
    /// as a whole; GitLab posts one discussion per comment and reports each rejected comment. Failures carry the
    /// <see cref="CommentPostErrors"/> codes; this call never retries.
    /// </summary>
    /// <remarks>Providers without inline comments keep this default, which fails permanently without any
    /// provider call.</remarks>
    Task<Result<InlineReviewPosting>> PostInlineReviewAsync(
        RepositorySlug slug,
        string pullRequestUrl,
        string commitSha,
        string summary,
        IReadOnlyList<InlineComment> comments,
        CancellationToken cancellationToken)
        => Task.FromResult(Result<InlineReviewPosting>.Fail(CommentPostErrors.NotSupported()));

    /// <summary>
    /// Records that <paramref name="blockerNumber"/> blocks <paramref name="issueNumber"/>. A failure is
    /// <see cref="BlockerLinkErrors.TransientCode"/> (5xx, 429, a rate-limited 403, a transport failure) or
    /// <see cref="BlockerLinkErrors.PermanentCode"/> (any other 4xx); this call never retries. A duplicate-link
    /// response is success only when a read-back of the issue's links shows the blocking relation.
    /// </summary>
    /// <remarks>Providers without blocker links keep this default, which fails permanently without any
    /// provider call.</remarks>
    Task<Result> AddBlockerAsync(
        RepositorySlug slug,
        int issueNumber,
        int blockerNumber,
        CancellationToken cancellationToken)
        => Task.FromResult(Result.Fail(BlockerLinkErrors.NotSupported()));

    /// <summary>
    /// Opens a draft pull/merge request from <paramref name="sourceBranch"/> into the repository's default branch
    /// and returns its web URL. When the provider refuses because one is already open for the branch, returns that
    /// one's URL, so a retried publish is idempotent. A failure is
    /// <see cref="RunPublishingErrors.TransientCode"/> or <see cref="RunPublishingErrors.PermanentCode"/>, except
    /// that a failed default-branch read keeps the provider's own error; this call never retries.
    /// </summary>
    /// <remarks>Providers without pull request creation keep this default, which fails permanently without any
    /// provider call.</remarks>
    Task<Result<string>> CreatePullRequestAsync(
        RepositorySlug slug,
        string sourceBranch,
        string title,
        string body,
        CancellationToken cancellationToken)
        => Task.FromResult(Result<string>.Fail(
            RunPublishingErrors.Permanent("This provider does not support opening pull requests.")));

    /// <summary>
    /// Marks the draft pull/merge request at <paramref name="pullRequestUrl"/> ready for review. One that is
    /// already ready succeeds without a change. GitLab has no draft attribute on its update endpoint, so only a
    /// leading <c>Draft:</c>, <c>[Draft]</c> or <c>(Draft)</c> is removed from the title and the rest is kept as
    /// a person may have edited it. A failure is <see cref="RunPublishingErrors.TransientCode"/> or
    /// <see cref="RunPublishingErrors.PermanentCode"/>; this call never retries.
    /// </summary>
    /// <remarks>Providers without draft support keep this default, which fails permanently without any
    /// provider call.</remarks>
    Task<Result> MarkPullRequestReadyAsync(
        RepositorySlug slug,
        string pullRequestUrl,
        CancellationToken cancellationToken)
        => Task.FromResult(Result.Fail(
            RunPublishingErrors.Permanent("This provider does not support marking pull requests ready.")));

    /// <summary>
    /// The commit identity of the account whose token this provider holds (see
    /// <see cref="IRunPublishingCommands.GetCommitIdentityAsync"/> for the per-provider rule).
    /// A failure is <see cref="RunPublishingErrors.TransientCode"/> or <see cref="RunPublishingErrors.PermanentCode"/>.
    /// </summary>
    /// <remarks>Providers without an identity read keep this default, which fails permanently without any
    /// provider call.</remarks>
    Task<Result<CommitIdentity>> GetAuthenticatedIdentityAsync(CancellationToken cancellationToken)
        => Task.FromResult(Result<CommitIdentity>.Fail(
            RunPublishingErrors.Permanent("This provider does not support reading the account identity.")));

    /// <summary>
    /// Whether <paramref name="ancestorRef"/> (a branch or commit) is an ancestor of, or equal to, the head of
    /// <paramref name="branchName"/>. A missing ref fails with <see cref="RunPublishingErrors.RefNotFoundCode"/>;
    /// any other failure is <see cref="RunPublishingErrors.TransientCode"/> or
    /// <see cref="RunPublishingErrors.PermanentCode"/>.
    /// </summary>
    /// <remarks>Providers without an ancestry read keep this default, which fails permanently without any
    /// provider call.</remarks>
    Task<Result<bool>> IsAncestorAsync(
        RepositorySlug slug,
        string ancestorRef,
        string branchName,
        CancellationToken cancellationToken)
        => Task.FromResult(Result<bool>.Fail(
            RunPublishingErrors.Permanent("This provider does not support comparing refs.")));

    /// <summary>
    /// Lists the repository's root directory on its default branch, without descending into subdirectories:
    /// each entry's name, kind and blob SHA. <paramref name="ifNoneMatch"/> is the ETag of the previous listing;
    /// a provider that supports conditional requests answers <see cref="RootDirectoryListing.Unchanged"/> when
    /// nothing changed since it.
    /// </summary>
    /// <remarks>Providers without a root listing keep this default, which fails with
    /// <see cref="RootDirectoryErrors.NotSupportedCode"/> without any provider call.</remarks>
    Task<Result<RootDirectoryListing>> ListRootDirectoryAsync(
        RepositorySlug slug,
        string? ifNoneMatch,
        CancellationToken cancellationToken)
        => Task.FromResult(Result<RootDirectoryListing>.Fail(RootDirectoryErrors.NotSupported()));

    /// <summary>
    /// Reads the raw content of the file whose blob SHA is <paramref name="blobSha"/>, as returned by
    /// <see cref="ListRootDirectoryAsync"/>, decoded as UTF-8.
    /// </summary>
    /// <remarks>Providers without a blob read keep this default, which fails with
    /// <see cref="RootDirectoryErrors.NotSupportedCode"/> without any provider call.</remarks>
    Task<Result<string>> ReadBlobAsync(
        RepositorySlug slug,
        string blobSha,
        CancellationToken cancellationToken)
        => Task.FromResult(Result<string>.Fail(RootDirectoryErrors.NotSupported()));

    Task<Result<BranchProtection>> GetBranchProtectionAsync(
        RepositorySlug slug,
        CancellationToken cancellationToken);

    /// <summary>
    /// Writes the default-branch rules that eligibility checks (reject direct pushes, force pushes
    /// and deletion). Idempotent: re-applying never duplicates provider objects. Callers must
    /// re-read the rules afterwards rather than trust this call's status — the write succeeding
    /// does not prove the rules are effective. A 403 fails with
    /// <see cref="BranchRuleWriteErrors.InsufficientPermissionCode"/>.
    /// </summary>
    Task<Result> ApplyBranchRulesAsync(
        RepositorySlug slug,
        CancellationToken cancellationToken);

    /// <summary>
    /// Creates or updates the repository webhook described by <paramref name="spec"/> with exactly
    /// Foundry's mapped event list, JSON payloads and the repository's secret. Every hook whose URL
    /// carries the repository's hook id (<see cref="WebhookHookSpec.Matches"/>) belongs to the
    /// repository: one is updated in place — events, secret, URL and active flag — preferring an exact
    /// payload-URL match, and the others are deleted; with none, a new hook is created. Callers pass a
    /// provider built with the account token. A missing permission fails with
    /// <see cref="WebhookWriteErrors.InsufficientPermissionCode"/>.
    /// </summary>
    /// <remarks>Providers without webhook support keep this default, which fails with
    /// <see cref="WebhookWriteErrors.NotSupportedCode"/> without any provider call.</remarks>
    Task<Result<WebhookUpsert>> UpsertWebhookAsync(
        RepositorySlug slug,
        WebhookHookSpec spec,
        CancellationToken cancellationToken)
        => Task.FromResult(Result<WebhookUpsert>.Fail(WebhookWriteErrors.NotSupported()));

    /// <summary>
    /// Asks the provider to send its own test delivery to the hook <paramref name="providerHookId"/>
    /// (<see cref="WebhookUpsert.ProviderHookId"/>): GitHub's ping, GitLab's test push event. The delivery
    /// arrives at the webhook endpoint like any other, so a reachable hook turns <c>Active</c>.
    /// </summary>
    /// <remarks>Providers without webhook support keep this default, which fails with
    /// <see cref="WebhookWriteErrors.NotSupportedCode"/> without any provider call.</remarks>
    Task<Result> TestWebhookAsync(
        RepositorySlug slug,
        long providerHookId,
        CancellationToken cancellationToken)
        => Task.FromResult(Result.Fail(WebhookWriteErrors.NotSupported()));

    /// <summary>
    /// Deletes every repository hook whose URL carries <paramref name="hookId"/>
    /// (<see cref="WebhookHookPath"/>) and returns how many were deleted. A missing permission fails
    /// with <see cref="WebhookWriteErrors.InsufficientPermissionCode"/>.
    /// </summary>
    /// <remarks>Providers without webhook support keep this default, which fails with
    /// <see cref="WebhookWriteErrors.NotSupportedCode"/> without any provider call.</remarks>
    Task<Result<int>> DeleteWebhooksAsync(
        RepositorySlug slug,
        string hookId,
        CancellationToken cancellationToken)
        => Task.FromResult(Result<int>.Fail(WebhookWriteErrors.NotSupported()));

    Task<Result<bool>> CreateBranchAsync(
        RepositorySlug slug,
        string branchName,
        CancellationToken cancellationToken);

    /// <summary>
    /// Deletes a work branch so the next dispatch starts from the default branch. Returns
    /// <c>true</c> when the branch was deleted and <c>false</c> when it was already absent.
    /// Refuses to delete the repository's default branch
    /// (<see cref="BranchDeletionErrors.DefaultBranchCode"/>).
    /// </summary>
    Task<Result<bool>> DeleteBranchAsync(
        RepositorySlug slug,
        string branchName,
        CancellationToken cancellationToken);

    Task<Result<MergeRequestByBranch>> GetMergeRequestByBranchAsync(
        RepositorySlug slug,
        string branchName,
        CommitSha? publishedHead,
        CancellationToken cancellationToken);

    Task<Result<BranchCommitSummary>> GetBranchCommitSummaryAsync(
        RepositorySlug slug,
        string branchName,
        CancellationToken cancellationToken);

    Task<Result<bool>> CanPushAsync(
        RepositorySlug slug,
        CancellationToken cancellationToken);

    Task<Result<ProviderChecks>> GetChecksAsync(
        RepositorySlug slug,
        string pullRequestUrl,
        CancellationToken cancellationToken);

    /// <summary>
    /// Resolves the CI job a failing check stands for, so its log can be read with <see cref="GetJobLogAsync"/>.
    /// A check that is not a CI job the provider exposes logs for (a commit status, a third-party check app)
    /// answers <see cref="CiJobLookup.NotACiJob"/>.
    /// </summary>
    /// <remarks>Providers without job logs keep this default, which answers
    /// <see cref="CiJobLookup.NotACiJob"/> without any provider call.</remarks>
    Task<Result<CiJobLookup>> ResolveCiJobAsync(
        RepositorySlug slug,
        FailingCheck check,
        CancellationToken cancellationToken)
        => Task.FromResult(Result<CiJobLookup>.Ok(new CiJobLookup.NotACiJob()));

    /// <summary>Reads the log of the CI job <paramref name="jobId"/>: its tail and the failure lines near its start.</summary>
    /// <remarks>Providers without job logs keep this default, which fails with
    /// <see cref="JobLogErrors.NotSupportedCode"/> without any provider call.</remarks>
    Task<Result<CiJobLog.Available>> GetJobLogAsync(
        RepositorySlug slug,
        long jobId,
        CancellationToken cancellationToken)
        => Task.FromResult(Result<CiJobLog.Available>.Fail(JobLogErrors.NotSupported()));

    /// <summary>
    /// Reads the pull request branch head and how far it is behind its base branch, for the post-run rebase
    /// (ADR 0087). A <c>Result</c> failure is transient and retried; a permanent refusal is
    /// <see cref="PullRequestRebaseState.Refused"/>.
    /// </summary>
    /// <remarks>Providers without a rebase implementation keep this default, which answers
    /// <see cref="PullRequestRebaseState.Unsupported"/> without any provider call.</remarks>
    Task<Result<PullRequestRebaseState>> GetRebaseStateAsync(
        RepositorySlug slug,
        string pullRequestUrl,
        CancellationToken cancellationToken)
        => Task.FromResult(Result<PullRequestRebaseState>.Ok(new PullRequestRebaseState.Unsupported()));

    /// <summary>
    /// Asks the provider to rebase the pull request branch onto its base server-side, guarded by
    /// <paramref name="expectedHeadSha"/> (ADR 0087). The rebase lands asynchronously: callers wait for a new
    /// head rather than trusting this call. A <c>Result</c> failure is transient and retried.
    /// </summary>
    /// <remarks>Providers without a rebase implementation keep this default, which answers
    /// <see cref="RebaseRequestOutcome.Unsupported"/> without any provider call.</remarks>
    Task<Result<RebaseRequestOutcome>> RequestRebaseAsync(
        RepositorySlug slug,
        string pullRequestId,
        string expectedHeadSha,
        CancellationToken cancellationToken)
        => Task.FromResult(Result<RebaseRequestOutcome>.Ok(new RebaseRequestOutcome.Unsupported()));

    /// <summary>
    /// Merges the pull request Foundry opened, pinned to <paramref name="expectedHeadSha"/>, the head its checks
    /// gate verified (ADR 0105). A <c>Result</c> failure is transient (5xx, timeout, rate limit) and retried;
    /// every <see cref="MergeRequestOutcome"/> is a definite answer.
    /// </summary>
    /// <remarks>Providers without a merge implementation keep this default, which answers
    /// <see cref="MergeRequestOutcome.Unsupported"/> without any provider call.</remarks>
    /// <exception cref="ArgumentException"><paramref name="expectedHeadSha"/> is null or blank.</exception>
    Task<Result<MergeRequestOutcome>> MergePullRequestAsync(
        RepositorySlug slug,
        string pullRequestUrl,
        string expectedHeadSha,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedHeadSha);
        return Task.FromResult(Result<MergeRequestOutcome>.Ok(new MergeRequestOutcome.Unsupported()));
    }
}
