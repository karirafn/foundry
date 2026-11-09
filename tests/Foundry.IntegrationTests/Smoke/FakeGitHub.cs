using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Modules.Monitoring.Features.Providers.Feedback;
using Foundry.Modules.Monitoring.Infrastructure.GitHub;
using Foundry.Shared;

using ProviderChecks = Foundry.Modules.Monitoring.Features.Providers.Checks.Checks;

namespace Foundry.IntegrationTests.Smoke;

/// <summary>
/// One GitHub repository holding one open trigger-labelled issue, behind the provider seams production
/// uses (<see cref="IIssueProviderFactory"/> and <see cref="IGitHubWriteProber"/>). The repository is
/// writable and its default branch protected; the issue's pull request exists once the test opens it.
/// </summary>
internal sealed class FakeGitHub : IIssueProviderFactory, IIssueProvider, IGitHubWriteProber
{
    internal const string Owner = "smoke-owner";
    internal const string Slug = Owner + "/smoke-repo";
    internal const int IssueNumber = 42;
    internal const string PullRequestUrl = "https://github.com/" + Slug + "/pull/43";

    private static readonly ProviderIssue Issue = new(
        IssueNumber,
        "Smoke: drive an issue through its lifecycle",
        "smoke-author",
        $"https://github.com/{Slug}/issues/{IssueNumber}",
        ["foundry", "feature"],
        "feature");

    private volatile bool _pullRequestOpened;

    public void OpenPullRequest() => _pullRequestOpened = true;

    public IIssueProvider CreateProvider(Credential credential, string token) => this;

    public Task<Result<WritePermissionProbeResult>> ProbeWriteAccessAsync(
        Uri apiBaseUrl,
        RepositorySlug slug,
        string token,
        CancellationToken cancellationToken)
        => Task.FromResult(Result<WritePermissionProbeResult>.Ok(new WritePermissionProbeResult.Granted()));

    public Task<Result<IssueListing>> GetIssuesAsync(RepositorySlug slug, CancellationToken cancellationToken)
        => Task.FromResult(Result<IssueListing>.Ok(new IssueListing([Issue], IsComplete: true)));

    public Task<Result<IReadOnlyList<int>>> GetDependenciesAsync(RepositorySlug slug, int issueNumber, CancellationToken cancellationToken)
        => Task.FromResult(Result<IReadOnlyList<int>>.Ok([]));

    public Task<Result<bool>> IsIssueClosedAsync(RepositorySlug slug, int issueNumber, CancellationToken cancellationToken)
        => Task.FromResult(Result<bool>.Ok(false));

    public Task<Result<PullRequestStatus>> GetPullRequestStatusAsync(RepositorySlug slug, string pullRequestUrl, CancellationToken cancellationToken)
        => Task.FromResult(Result<PullRequestStatus>.Ok(new PullRequestStatus(IsClosed: false, IsMerged: false)));

    public Task<Result<ReviewFeedback>> GetReviewFeedbackAsync(RepositorySlug slug, string pullRequestUrl, DateTimeOffset since, CancellationToken cancellationToken)
        => Task.FromResult(Result<ReviewFeedback>.Ok(new ReviewFeedback([], OmittedCommentCount: 0, NewestCommentAt: null)));

    public Task<Result<BranchProtection>> GetBranchProtectionAsync(RepositorySlug slug, CancellationToken cancellationToken)
        => Task.FromResult(Result<BranchProtection>.Ok(new BranchProtection(
            "main",
            RejectDirectPushes: true,
            RejectForcePushes: true,
            RejectDeletion: true)));

    public Task<Result> ApplyBranchRulesAsync(RepositorySlug slug, CancellationToken cancellationToken)
        => Task.FromResult(Result.Ok());

    public Task<Result<bool>> CreateBranchAsync(RepositorySlug slug, string branchName, CancellationToken cancellationToken)
        => Task.FromResult(Result<bool>.Ok(true));

    public Task<Result<bool>> DeleteBranchAsync(RepositorySlug slug, string branchName, CancellationToken cancellationToken)
        => Task.FromResult(Result<bool>.Ok(true));

    public Task<Result<MergeRequestByBranch>> GetMergeRequestByBranchAsync(RepositorySlug slug, string branchName, CancellationToken cancellationToken)
        => Task.FromResult(Result<MergeRequestByBranch>.Ok(_pullRequestOpened
            ? new MergeRequestByBranch(MergeRequestPresence.Open, PullRequestUrl)
            : new MergeRequestByBranch(MergeRequestPresence.None, null)));

    public Task<Result<BranchCommitSummary>> GetBranchCommitSummaryAsync(RepositorySlug slug, string branchName, CancellationToken cancellationToken)
        => Task.FromResult(Result<BranchCommitSummary>.Ok(_pullRequestOpened
            ? new BranchCommitSummary(1, "0123456789abcdef0123456789abcdef01234567")
            : new BranchCommitSummary(0, null)));

    public Task<Result<bool>> CanPushAsync(RepositorySlug slug, CancellationToken cancellationToken)
        => Task.FromResult(Result<bool>.Ok(true));

    // Checks stay pending so the issue rests in checks-pending once the run is tracked.
    public Task<Result<ProviderChecks>> GetChecksAsync(RepositorySlug slug, string pullRequestUrl, CancellationToken cancellationToken)
        => Task.FromResult(Result<ProviderChecks>.Ok(new ProviderChecks.Pending()));
}
