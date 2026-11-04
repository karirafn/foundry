using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using ProviderChecks = Foundry.Modules.Monitoring.Features.Providers.Checks.Checks;
using Foundry.Modules.Monitoring.Features.Providers.Feedback;
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

    Task<Result<ReviewFeedback>> GetReviewFeedbackAsync(
        RepositorySlug slug,
        string pullRequestUrl,
        DateTimeOffset since,
        CancellationToken cancellationToken);

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
}
