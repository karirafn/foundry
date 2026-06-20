using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.RepositoryEligibilityEvaluatorTests;

public sealed class EvaluateAndStoreAsync
{
    private static MonitoredRepository CreateRepo()
    {
        RepositorySlug slug = ((Result<RepositorySlug>.Success)RepositorySlug.Create("owner/repo")).Value;
        return MonitoredRepository.Create(slug, AccountId.New(), pollInterval: null);
    }

    [Fact]
    public async Task WhenProviderReturnsFullProtection_SetsEligibilityToEligible()
    {
        // Arrange
        MonitoredRepository repo = CreateRepo();
        BranchProtection protection = new("main", RejectDirectPushes: true, RejectForcePushes: true, RejectDeletion: true);
        StubIssueProvider provider = new(Result<BranchProtection>.Ok(protection));
        RepositoryEligibilityEvaluator sut = new();

        // Act
        await sut.EvaluateAndStoreAsync(repo, provider, CancellationToken.None);

        // Assert
        repo.Eligibility.ShouldBeOfType<RepositoryEligibility.Eligible>();
    }

    [Fact]
    public async Task WhenProviderReturnsViolations_SetsEligibilityToIneligible()
    {
        // Arrange
        MonitoredRepository repo = CreateRepo();
        BranchProtection protection = new("main", RejectDirectPushes: false, RejectForcePushes: true, RejectDeletion: true);
        StubIssueProvider provider = new(Result<BranchProtection>.Ok(protection));
        RepositoryEligibilityEvaluator sut = new();

        // Act
        await sut.EvaluateAndStoreAsync(repo, provider, CancellationToken.None);

        // Assert
        RepositoryEligibility.Ineligible ineligible = repo.Eligibility.ShouldBeOfType<RepositoryEligibility.Ineligible>();
        ineligible.Violations.ShouldContain(v => v.Rule == EligibilityViolation.AllowDirectPushesRule);
    }

    [Fact]
    public async Task WhenProviderFails_SetsEligibilityToUnreachable()
    {
        // Arrange
        MonitoredRepository repo = CreateRepo();
        StubIssueProvider provider = new(
            Result<BranchProtection>.Fail(new Error("Provider.Error", "Unreachable")));
        RepositoryEligibilityEvaluator sut = new();

        // Act
        await sut.EvaluateAndStoreAsync(repo, provider, CancellationToken.None);

        // Assert
        repo.Eligibility.ShouldBeOfType<RepositoryEligibility.Unreachable>();
    }

    private sealed class StubIssueProvider(Result<BranchProtection> branchProtectionResult) : IIssueProvider
    {
        public Task<Result<IReadOnlyList<ProviderIssue>>> GetIssuesAsync(
            RepositorySlug slug,
            CancellationToken cancellationToken)
            => Task.FromResult(Result<IReadOnlyList<ProviderIssue>>.Ok([]));

        public Task<Result<IReadOnlyList<int>>> GetDependenciesAsync(
            RepositorySlug slug,
            int issueNumber,
            CancellationToken cancellationToken)
            => Task.FromResult(Result<IReadOnlyList<int>>.Ok([]));

        public Task<Result<bool>> IsIssueClosedAsync(
            RepositorySlug slug,
            int issueNumber,
            CancellationToken cancellationToken)
            => Task.FromResult(Result<bool>.Ok(false));

        public Task<Result<PullRequestStatus>> GetPullRequestStatusAsync(
            RepositorySlug slug,
            string pullRequestUrl,
            CancellationToken cancellationToken)
            => Task.FromResult(Result<PullRequestStatus>.Ok(new PullRequestStatus(IsClosed: false, IsMerged: false)));

        public Task<Result<ReviewFeedback>> GetReviewFeedbackAsync(
            RepositorySlug slug,
            string pullRequestUrl,
            DateTimeOffset since,
            CancellationToken cancellationToken)
            => Task.FromResult(Result<ReviewFeedback>.Ok(new ReviewFeedback([])));

        public Task<Result<BranchProtection>> GetBranchProtectionAsync(
            RepositorySlug slug,
            CancellationToken cancellationToken)
            => Task.FromResult(branchProtectionResult);

        public Task<Result<bool>> CreateBranchAsync(
            RepositorySlug slug,
            string branchName,
            CancellationToken cancellationToken)
            => Task.FromResult(Result<bool>.Ok(true));

        public Task<Result<bool>> HasBranchCommitsAsync(
            RepositorySlug slug,
            string branchName,
            CancellationToken cancellationToken)
            => Task.FromResult(Result<bool>.Ok(false));

        public Task<Result<string>> GetPullRequestByBranchAsync(
            RepositorySlug slug,
            string branchName,
            CancellationToken cancellationToken)
            => Task.FromResult(Result<string>.Ok(string.Empty));
    }
}
