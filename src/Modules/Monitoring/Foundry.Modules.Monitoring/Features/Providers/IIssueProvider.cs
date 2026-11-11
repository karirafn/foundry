using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers.Checks;
using Foundry.Modules.Monitoring.Features.Providers.Comments;
using ProviderChecks = Foundry.Modules.Monitoring.Features.Providers.Checks.Checks;
using Foundry.Modules.Monitoring.Features.Providers.Feedback;
using Foundry.Modules.Monitoring.Features.Providers.Rebase;
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

    Task<Result<PullRequestStatus>> GetPullRequestStatusAsync(
        RepositorySlug slug,
        string pullRequestUrl,
        CancellationToken cancellationToken);

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
    /// Foundry's mapped event list, JSON payloads and the repository's secret. A hook whose payload
    /// URL matches <see cref="WebhookHookSpec.PayloadUrl"/> (or one of
    /// <see cref="WebhookHookSpec.PreviousPayloadUrls"/>) is updated in place — events, secret, URL and
    /// active flag — and adopted; otherwise a new hook is created. Callers pass a provider built with
    /// the account's server-side token, never the worker token. A missing permission fails with
    /// <see cref="WebhookWriteErrors.InsufficientPermissionCode"/>.
    /// </summary>
    /// <remarks>Providers without webhook support keep this default, which fails with
    /// <see cref="WebhookWriteErrors.NotSupportedCode"/> without any provider call.</remarks>
    Task<Result<WebhookUpsertKind>> UpsertWebhookAsync(
        RepositorySlug slug,
        WebhookHookSpec spec,
        CancellationToken cancellationToken)
        => Task.FromResult(Result<WebhookUpsertKind>.Fail(WebhookWriteErrors.NotSupported()));

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

    /// <summary>Reads the full log text of the CI job <paramref name="jobId"/>.</summary>
    /// <remarks>Providers without job logs keep this default, which fails with
    /// <see cref="JobLogErrors.NotSupportedCode"/> without any provider call.</remarks>
    Task<Result<string>> GetJobLogAsync(
        RepositorySlug slug,
        long jobId,
        CancellationToken cancellationToken)
        => Task.FromResult(Result<string>.Fail(JobLogErrors.NotSupported()));

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
}
