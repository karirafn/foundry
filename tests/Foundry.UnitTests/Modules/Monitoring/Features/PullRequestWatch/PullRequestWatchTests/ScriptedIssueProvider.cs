using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Modules.Monitoring.Features.Providers.Checks;
using Foundry.Modules.Monitoring.Features.Providers.Comments;
using Foundry.Modules.Monitoring.Features.Providers.Feedback;
using Foundry.Shared;

namespace Foundry.UnitTests.Modules.Monitoring.Features.PullRequestWatch.PullRequestWatchTests;

/// <summary>An issue provider answering the watch's two reads from scripted results, counting each read.</summary>
internal sealed class ScriptedIssueProvider : IIssueProvider
{
    public Result<bool> IssueClosedResult { get; set; } = Result<bool>.Ok(false);

    public Result<PullRequestState> PullRequestStateResult { get; set; } =
        Result<PullRequestState>.Ok(new PullRequestState.Open(CommitSha.From(new string('a', 40)), IsDraft: false));

    public int IssueReads { get; private set; }

    public int PullRequestReads { get; private set; }

    public Task<Result<bool>> IsIssueClosedAsync(
        RepositorySlug slug,
        int issueNumber,
        CancellationToken cancellationToken)
    {
        IssueReads++;
        return Task.FromResult(IssueClosedResult);
    }

    public Task<Result<PullRequestState>> GetPullRequestStateAsync(
        RepositorySlug slug,
        string pullRequestUrl,
        CancellationToken cancellationToken)
    {
        PullRequestReads++;
        return Task.FromResult(PullRequestStateResult);
    }

    public Task<Result<IssueListing>> GetIssuesAsync(RepositorySlug slug, CancellationToken cancellationToken)
        => throw new NotSupportedException();

    public Task<Result<IReadOnlyList<int>>> GetDependenciesAsync(
        RepositorySlug slug,
        int issueNumber,
        CancellationToken cancellationToken)
        => throw new NotSupportedException();

    public Task<Result<ReviewFeedback>> GetReviewFeedbackAsync(
        RepositorySlug slug,
        string pullRequestUrl,
        DateTimeOffset since,
        PostedCommentIds postedComments,
        CancellationToken cancellationToken)
        => throw new NotSupportedException();

    public Task<Result<BranchProtection>> GetBranchProtectionAsync(RepositorySlug slug, CancellationToken cancellationToken)
        => throw new NotSupportedException();

    public Task<Result> ApplyBranchRulesAsync(RepositorySlug slug, CancellationToken cancellationToken)
        => throw new NotSupportedException();

    public Task<Result<bool>> CreateBranchAsync(RepositorySlug slug, string branchName, CancellationToken cancellationToken)
        => throw new NotSupportedException();

    public Task<Result<bool>> DeleteBranchAsync(RepositorySlug slug, string branchName, CancellationToken cancellationToken)
        => throw new NotSupportedException();

    public Task<Result<MergeRequestByBranch>> GetMergeRequestByBranchAsync(
        RepositorySlug slug,
        string branchName,
        CommitSha? publishedHead,
        CancellationToken cancellationToken)
        => throw new NotSupportedException();

    public Task<Result<BranchCommitSummary>> GetBranchCommitSummaryAsync(
        RepositorySlug slug,
        string branchName,
        CancellationToken cancellationToken)
        => throw new NotSupportedException();

    public Task<Result<bool>> CanPushAsync(RepositorySlug slug, CancellationToken cancellationToken)
        => throw new NotSupportedException();

    public Task<Result<Checks>> GetChecksAsync(RepositorySlug slug, string pullRequestUrl, CancellationToken cancellationToken)
        => throw new NotSupportedException();
}
