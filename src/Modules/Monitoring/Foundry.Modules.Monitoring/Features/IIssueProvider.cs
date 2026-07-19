using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Features;

public interface IIssueProvider
{
    Task<Result<IReadOnlyList<ProviderIssue>>> GetIssuesAsync(
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

    Task<Result<ReviewFeedback>> GetReviewFeedbackAsync(
        RepositorySlug slug,
        string pullRequestUrl,
        DateTimeOffset since,
        CancellationToken cancellationToken);

    Task<Result<BranchProtection>> GetBranchProtectionAsync(
        RepositorySlug slug,
        CancellationToken cancellationToken);

    Task<Result<bool>> CreateBranchAsync(
        RepositorySlug slug,
        string branchName,
        CancellationToken cancellationToken);

    Task<Result<bool>> HasBranchCommitsAsync(
        RepositorySlug slug,
        string branchName,
        CancellationToken cancellationToken);

    Task<Result<MergeRequestByBranch>> GetMergeRequestByBranchAsync(
        RepositorySlug slug,
        string branchName,
        CancellationToken cancellationToken);

    Task<Result<LatestBranchCommit>> GetLatestBranchCommitAsync(
        RepositorySlug slug,
        string branchName,
        CancellationToken cancellationToken);

    Task<Result<bool>> CanPushAsync(
        RepositorySlug slug,
        CancellationToken cancellationToken);
}
