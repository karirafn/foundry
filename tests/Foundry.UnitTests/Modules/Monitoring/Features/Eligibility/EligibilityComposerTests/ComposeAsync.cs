using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Eligibility;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Shared;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.Eligibility.EligibilityComposerTests;

public sealed class ComposeAsync
{
    private static readonly RepositorySlug Slug = RepositorySlug.Create("owner/repo").ValueOrThrow();

    private static (EligibilityComposer Composer, GitHubCredential Credential) BuildSut(
        Result<BranchProtection>? branchProtectionResult = null)
    {
        Result<BranchProtection> protectionResult = branchProtectionResult
            ?? Result<BranchProtection>.Ok(new BranchProtection("main", true, true, true));

        EligibilityComposer composer = new(new StubProviderFactory(protectionResult));
        GitHubCredential credential = GitHubCredential.Create(
            "test",
            "ghp_token",
            BaseUrl.Create("https://github.com").ValueOrThrow());

        return (composer, credential);
    }

    [Fact]
    public async Task WhenVerdictIsUnknownTransport_ReturnsUnreachableNeverProbed()
    {
        // Arrange
        (EligibilityComposer composer, GitHubCredential credential) = BuildSut();
        WriteProbeVerdict verdict = new WriteProbeVerdict.Unknown(Reason: UnknownReason.Transport);

        // Act
        RepositoryEligibility eligibility = await composer.ComposeAsync(
            Slug, verdict, credential, "ghp_token", CancellationToken.None);

        // Assert
        RepositoryEligibility.Unreachable unreachable = eligibility.ShouldBeOfType<RepositoryEligibility.Unreachable>();
        unreachable.Reason.ShouldBe(UnreachableReason.NeverProbed);
    }

    [Fact]
    public async Task WhenVerdictIsUnknownRateLimited_ReturnsUnreachableRateLimited()
    {
        // Arrange
        (EligibilityComposer composer, GitHubCredential credential) = BuildSut();
        WriteProbeVerdict verdict = new WriteProbeVerdict.Unknown(Reason: UnknownReason.RateLimited);

        // Act
        RepositoryEligibility eligibility = await composer.ComposeAsync(
            Slug, verdict, credential, "ghp_token", CancellationToken.None);

        // Assert
        RepositoryEligibility.Unreachable unreachable = eligibility.ShouldBeOfType<RepositoryEligibility.Unreachable>();
        unreachable.Reason.ShouldBe(UnreachableReason.RateLimited);
    }

    [Fact]
    public async Task WhenVerdictIsDenied_ReturnsIneligibleWithCannotPushViolation()
    {
        // Arrange
        (EligibilityComposer composer, GitHubCredential credential) = BuildSut();
        WriteProbeVerdict verdict = new WriteProbeVerdict.Denied();

        // Act
        RepositoryEligibility eligibility = await composer.ComposeAsync(
            Slug, verdict, credential, "ghp_token", CancellationToken.None);

        // Assert
        RepositoryEligibility.Ineligible ineligible = eligibility.ShouldBeOfType<RepositoryEligibility.Ineligible>();
        ineligible.Violations.ShouldContain(v => v.Rule.StartsWith("cannot-push", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WhenVerdictIsGranted_AndBranchProtectionFails_ReturnsUnreachableBranchRulesUnavailable()
    {
        // Arrange
        (EligibilityComposer composer, GitHubCredential credential) = BuildSut(
            Result<BranchProtection>.Fail(new Error("Provider.Error", "503 Service Unavailable")));
        WriteProbeVerdict verdict = new WriteProbeVerdict.Granted();

        // Act
        RepositoryEligibility eligibility = await composer.ComposeAsync(
            Slug, verdict, credential, "ghp_token", CancellationToken.None);

        // Assert
        RepositoryEligibility.Unreachable unreachable = eligibility.ShouldBeOfType<RepositoryEligibility.Unreachable>();
        unreachable.Reason.ShouldBe(UnreachableReason.BranchRulesUnavailable);
    }

    private sealed class StubProviderFactory(Result<BranchProtection> branchProtectionResult) : IIssueProviderFactory
    {
        public IIssueProvider CreateProvider(Credential credential, string token) =>
            new StubProvider(branchProtectionResult);
    }

    private sealed class StubProvider(Result<BranchProtection> branchProtectionResult) : IIssueProvider
    {
        public Task<Result<BranchProtection>> GetBranchProtectionAsync(
            RepositorySlug slug,
            CancellationToken cancellationToken)
            => Task.FromResult(branchProtectionResult);

        public Task<Result<IssueListing>> GetIssuesAsync(RepositorySlug slug, CancellationToken cancellationToken)
            => Task.FromResult(Result<IssueListing>.Ok(new IssueListing([], IsComplete: true)));

        public Task<Result<IReadOnlyList<int>>> GetDependenciesAsync(
            RepositorySlug slug, int issueNumber, CancellationToken cancellationToken)
            => Task.FromResult(Result<IReadOnlyList<int>>.Ok([]));

        public Task<Result<bool>> IsIssueClosedAsync(
            RepositorySlug slug, int issueNumber, CancellationToken cancellationToken)
            => Task.FromResult(Result<bool>.Ok(false));

        public Task<Result<PullRequestStatus>> GetPullRequestStatusAsync(
            RepositorySlug slug, string pullRequestUrl, CancellationToken cancellationToken)
            => Task.FromResult(Result<PullRequestStatus>.Ok(new PullRequestStatus(false, false)));

        public Task<Result<ReviewFeedback>> GetReviewFeedbackAsync(
            RepositorySlug slug, string pullRequestUrl, DateTimeOffset since, CancellationToken cancellationToken)
            => Task.FromResult(Result<ReviewFeedback>.Ok(new ReviewFeedback([])));

        public Task<Result<bool>> CreateBranchAsync(
            RepositorySlug slug, string branchName, CancellationToken cancellationToken)
            => Task.FromResult(Result<bool>.Ok(true));

        public Task<Result<MergeRequestByBranch>> GetMergeRequestByBranchAsync(
            RepositorySlug slug, string branchName, CancellationToken cancellationToken)
            => Task.FromResult(Result<MergeRequestByBranch>.Ok(new MergeRequestByBranch(MergeRequestPresence.None, null)));

        public Task<Result<BranchCommitSummary>> GetBranchCommitSummaryAsync(
            RepositorySlug slug, string branchName, CancellationToken cancellationToken)
            => Task.FromResult(Result<BranchCommitSummary>.Fail(new Error("Provider.NoCommit", "No commit found")));

        public Task<Result<bool>> CanPushAsync(RepositorySlug slug, CancellationToken cancellationToken)
            => Task.FromResult(Result<bool>.Ok(true));
    }
}
