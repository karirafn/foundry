using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Modules.Monitoring.Features.Providers.Checks;
using Foundry.Modules.Monitoring.Features.Providers.Comments;
using Foundry.Modules.Monitoring.Features.Providers.Feedback;
using Foundry.Shared;

namespace Foundry.Testing;

/// <summary>
/// A test double for <see cref="IIssueProvider"/> that counts every provider call
/// and returns configurable data. Use with the real <c>RepositoryPoller.PollAsync</c>
/// to assert invariance — the counter lives on the fake, not on production code.
/// </summary>
internal sealed class CountingIssueProvider : IIssueProvider
{
    private readonly IReadOnlyList<ProviderIssue> _issues;
    private readonly IReadOnlyList<int> _dependencies;

    internal CountingIssueProvider(IReadOnlyList<ProviderIssue>? issues = null, IReadOnlyList<int>? dependencies = null)
    {
        _issues = issues ?? [];
        _dependencies = dependencies ?? [];
    }

    public int TotalCalls { get; private set; }
    public int GetIssuesCallCount { get; private set; }
    public int GetDependenciesCallCount { get; private set; }
    public int IsIssueClosedCallCount { get; private set; }
    public int GetPullRequestStatusCallCount { get; private set; }
    public int GetReviewFeedbackCallCount { get; private set; }
    public int GetBranchProtectionCallCount { get; private set; }
    public int CreateBranchCallCount { get; private set; }
    public int DeleteBranchCallCount { get; private set; }
    public int ApplyBranchRulesCallCount { get; private set; }
    public int UpsertWebhookCallCount { get; private set; }
    public int GetMergeRequestByBranchCallCount { get; private set; }
    public int GetBranchCommitSummaryCallCount { get; private set; }
    public int CanPushCallCount { get; private set; }
    public int GetChecksCallCount { get; private set; }

    /// <summary>When set, the listing call fails with this error instead of returning the configured issues.</summary>
    public Error? ListingError { get; set; }

    public Task<Result<IssueListing>> GetIssuesAsync(
        RepositorySlug slug,
        CancellationToken cancellationToken)
    {
        TotalCalls++;
        GetIssuesCallCount++;
        if (ListingError is not null)
        {
            return Task.FromResult(Result<IssueListing>.Fail(ListingError));
        }

        return Task.FromResult(Result<IssueListing>.Ok(new IssueListing(_issues, IsComplete: true)));
    }

    public Task<Result<IReadOnlyList<int>>> GetDependenciesAsync(
        RepositorySlug slug,
        int issueNumber,
        CancellationToken cancellationToken)
    {
        TotalCalls++;
        GetDependenciesCallCount++;
        return Task.FromResult(Result<IReadOnlyList<int>>.Ok(_dependencies));
    }

    public Task<Result<bool>> IsIssueClosedAsync(
        RepositorySlug slug,
        int issueNumber,
        CancellationToken cancellationToken)
    {
        TotalCalls++;
        IsIssueClosedCallCount++;
        return Task.FromResult(Result<bool>.Ok(false));
    }

    public Task<Result<PullRequestStatus>> GetPullRequestStatusAsync(
        RepositorySlug slug,
        string pullRequestUrl,
        CancellationToken cancellationToken)
    {
        TotalCalls++;
        GetPullRequestStatusCallCount++;
        return Task.FromResult(
            Result<PullRequestStatus>.Ok(new PullRequestStatus(IsClosed: false, IsMerged: false)));
    }

    public Task<Result<ReviewFeedback>> GetReviewFeedbackAsync(
        RepositorySlug slug,
        string pullRequestUrl,
        DateTimeOffset since,
        PostedCommentIds postedComments,
        CancellationToken cancellationToken)
    {
        TotalCalls++;
        GetReviewFeedbackCallCount++;
        return Task.FromResult(Result<ReviewFeedback>.Ok(new ReviewFeedback([], OmittedCommentCount: 0, NewestCommentAt: null)));
    }

    public Task<Result<BranchProtection>> GetBranchProtectionAsync(
        RepositorySlug slug,
        CancellationToken cancellationToken)
    {
        TotalCalls++;
        GetBranchProtectionCallCount++;
        return Task.FromResult(
            Result<BranchProtection>.Ok(new BranchProtection("main", true, true, true)));
    }

    public Task<Result> ApplyBranchRulesAsync(

        RepositorySlug slug,

        CancellationToken cancellationToken)

    {

        TotalCalls++;

        ApplyBranchRulesCallCount++;

        return Task.FromResult(Result.Ok());

    }

    public Task<Result<WebhookUpsertKind>> UpsertWebhookAsync(
        RepositorySlug slug,
        WebhookHookSpec spec,
        CancellationToken cancellationToken)
    {
        TotalCalls++;
        UpsertWebhookCallCount++;
        return Task.FromResult(Result<WebhookUpsertKind>.Ok(WebhookUpsertKind.Updated));
    }


    public Task<Result<bool>> CreateBranchAsync(
        RepositorySlug slug,
        string branchName,
        CancellationToken cancellationToken)
    {
        TotalCalls++;
        CreateBranchCallCount++;
        return Task.FromResult(Result<bool>.Ok(true));
    }

    public Task<Result<bool>> DeleteBranchAsync(
        RepositorySlug slug,
        string branchName,
        CancellationToken cancellationToken)
    {
        TotalCalls++;
        DeleteBranchCallCount++;
        return Task.FromResult(Result<bool>.Ok(true));
    }

    public Task<Result<MergeRequestByBranch>> GetMergeRequestByBranchAsync(
        RepositorySlug slug,
        string branchName,
        CancellationToken cancellationToken)
    {
        TotalCalls++;
        GetMergeRequestByBranchCallCount++;
        return Task.FromResult(
            Result<MergeRequestByBranch>.Ok(new MergeRequestByBranch(MergeRequestPresence.None, null)));
    }

    public Task<Result<BranchCommitSummary>> GetBranchCommitSummaryAsync(
        RepositorySlug slug,
        string branchName,
        CancellationToken cancellationToken)
    {
        TotalCalls++;
        GetBranchCommitSummaryCallCount++;
        return Task.FromResult(
            Result<BranchCommitSummary>.Fail(new Error("Provider.NoCommit", "No commit found")));
    }

    public Task<Result<bool>> CanPushAsync(
        RepositorySlug slug,
        CancellationToken cancellationToken)
    {
        TotalCalls++;
        CanPushCallCount++;
        return Task.FromResult(Result<bool>.Ok(true));
    }

    public Task<Result<Checks>> GetChecksAsync(
        RepositorySlug slug,
        string pullRequestUrl,
        CancellationToken cancellationToken)
    {
        TotalCalls++;
        GetChecksCallCount++;
        return Task.FromResult(Result<Checks>.Ok(new Checks.NoneDetected()));
    }
}
