using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.CredentialResolution;
using Foundry.Modules.Monitoring.Features.Eligibility;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Modules.Monitoring.Infrastructure.GitHub;
using Foundry.Shared;
using Foundry.Testing;

using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.Eligibility.RepositoryEligibilityEvaluatorTests;

public sealed class EvaluateAndStoreAsync
{
    private static MonitoredRepository CreateRepo(string slug = "owner/repo")
    {
        RepositorySlug repositorySlug = RepositorySlug.Create(slug).ValueOrThrow();
        return MonitoredRepository.Create(repositorySlug, "github.com", pollInterval: null);
    }

    private static RepositoryEligibilityEvaluator CreateSut(
        ICredentialResolver? resolver = null,
        IIssueProviderFactory? providerFactory = null,
        IGitHubWriteProber? writeProber = null)
    {
        return new RepositoryEligibilityEvaluator(
            resolver ?? new NullCredentialResolver(),
            providerFactory ?? new NullProviderFactory(),
            writeProber ?? new GrantedWriteProber(),
            NullLogger<RepositoryEligibilityEvaluator>.Instance);
    }

    [Fact]
    public async Task WhenNoCredentialCoversRepo_SetsEligibilityToIneligibleWithNoCredentialViolation()
    {
        // Arrange
        MonitoredRepository repo = CreateRepo("myorg/repo");
        RepositoryEligibilityEvaluator sut = CreateSut(resolver: new NullCredentialResolver());

        // Act
        await sut.EvaluateAndStoreAsync(repo, CancellationToken.None);

        // Assert
        RepositoryEligibility.Ineligible ineligible = repo.Eligibility.ShouldBeOfType<RepositoryEligibility.Ineligible>();
        ineligible.Violations.ShouldHaveSingleItem();
        ineligible.Violations[0].Rule.ShouldBe("no-credential:myorg");
    }

    [Fact]
    public async Task WhenNoCredentialCoversRepo_DoesNotInvokeProviderFactory()
    {
        // Arrange
        MonitoredRepository repo = CreateRepo("myorg/repo");
        TrackingProviderFactory trackingFactory = new();
        RepositoryEligibilityEvaluator sut = CreateSut(
            resolver: new NullCredentialResolver(),
            providerFactory: trackingFactory);

        // Act
        await sut.EvaluateAndStoreAsync(repo, CancellationToken.None);

        // Assert
        trackingFactory.WasInvoked.ShouldBeFalse();
    }

    [Fact]
    public async Task WhenNoCredentialCoversRepo_ReportsTopLevelNamespace()
    {
        // Arrange — "a/b/c/repo" has top-level namespace "a" (last in PrefixesOf, which is longest-first)
        MonitoredRepository repo = CreateRepo("a/b/c/repo");
        RepositoryEligibilityEvaluator sut = CreateSut(resolver: new NullCredentialResolver());

        // Act
        await sut.EvaluateAndStoreAsync(repo, CancellationToken.None);

        // Assert
        RepositoryEligibility.Ineligible ineligible = repo.Eligibility.ShouldBeOfType<RepositoryEligibility.Ineligible>();
        ineligible.Violations[0].Rule.ShouldBe("no-credential:a");
    }

    [Fact]
    public async Task WhenGitHubCredentialCoversRepo_AndProbeGranted_AndBranchProtectionPasses_SetsEligibilityToEligible()
    {
        // Arrange
        MonitoredRepository repo = CreateRepo();
        BranchProtection protection = new("main", RejectDirectPushes: true, RejectForcePushes: true, RejectDeletion: true);
        GitHubCredential credential = GitHubCredential.Create(
            "test",
            "token",
            BaseUrl.Create("https://github.com").ValueOrThrow());
        RepositoryEligibilityEvaluator sut = CreateSut(
            resolver: new StubCredentialResolver(credential),
            providerFactory: new StubProviderFactory(Result<BranchProtection>.Ok(protection)),
            writeProber: new GrantedWriteProber());

        // Act
        await sut.EvaluateAndStoreAsync(repo, CancellationToken.None);

        // Assert
        repo.Eligibility.ShouldBeOfType<RepositoryEligibility.Eligible>();
    }

    [Fact]
    public async Task WhenGitHubProbeReturnsMissing_SetsEligibilityToIneligibleWithCannotPushViolation()
    {
        // Arrange
        MonitoredRepository repo = CreateRepo("owner/repo");
        GitHubCredential credential = GitHubCredential.Create(
            "test",
            "token",
            BaseUrl.Create("https://github.com").ValueOrThrow());
        BranchProtection protection = new("main", RejectDirectPushes: true, RejectForcePushes: true, RejectDeletion: true);
        StubProviderFactory factory = new(Result<BranchProtection>.Ok(protection));
        RepositoryEligibilityEvaluator sut = CreateSut(
            resolver: new StubCredentialResolver(credential),
            providerFactory: factory,
            writeProber: new MissingWriteProber(WritePermission.Contents));

        // Act
        await sut.EvaluateAndStoreAsync(repo, CancellationToken.None);

        // Assert
        RepositoryEligibility.Ineligible ineligible = repo.Eligibility.ShouldBeOfType<RepositoryEligibility.Ineligible>();
        ineligible.Violations.ShouldHaveSingleItem();
        ineligible.Violations[0].Rule.ShouldBe("cannot-push:owner/repo");
        factory.CreatedProvider.ShouldBeNull();
    }

    [Fact]
    public async Task WhenGitHubProbeReturnsTransportFailure_SetsEligibilityToUnreachable()
    {
        // Arrange
        MonitoredRepository repo = CreateRepo();
        GitHubCredential credential = GitHubCredential.Create(
            "test",
            "token",
            BaseUrl.Create("https://github.com").ValueOrThrow());
        RepositoryEligibilityEvaluator sut = CreateSut(
            resolver: new StubCredentialResolver(credential),
            providerFactory: new NullProviderFactory(),
            writeProber: new FailingWriteProber());

        // Act
        await sut.EvaluateAndStoreAsync(repo, CancellationToken.None);

        // Assert
        repo.Eligibility.ShouldBeOfType<RepositoryEligibility.Unreachable>();
    }

    [Fact]
    public async Task WhenGitLabCredentialCoversRepo_AndCanPushReturnsFalse_SetsEligibilityToIneligibleWithCannotPushViolation()
    {
        // Arrange
        MonitoredRepository repo = CreateRepo("owner/repo");
        GitLabCredential credential = GitLabCredential.Create(
            "test",
            "token",
            BaseUrl.Create("https://gitlab.com").ValueOrThrow());
        BranchProtection protection = new("main", RejectDirectPushes: true, RejectForcePushes: true, RejectDeletion: true);
        StubProviderFactory factory = new(
            Result<BranchProtection>.Ok(protection),
            canPushResult: Result<bool>.Ok(false));
        RepositoryEligibilityEvaluator sut = CreateSut(
            resolver: new StubCredentialResolver(credential),
            providerFactory: factory,
            writeProber: new GrantedWriteProber());

        // Act
        await sut.EvaluateAndStoreAsync(repo, CancellationToken.None);

        // Assert
        RepositoryEligibility.Ineligible ineligible = repo.Eligibility.ShouldBeOfType<RepositoryEligibility.Ineligible>();
        ineligible.Violations.ShouldHaveSingleItem();
        ineligible.Violations[0].Rule.ShouldBe("cannot-push:owner/repo");
        factory.CreatedProvider.ShouldNotBeNull();
        factory.CreatedProvider!.BranchProtectionWasCalled.ShouldBeFalse();
    }

    [Fact]
    public async Task WhenGitLabCredentialCoversRepo_AndCanPushFails_SetsEligibilityToUnreachable()
    {
        // Arrange
        MonitoredRepository repo = CreateRepo();
        GitLabCredential credential = GitLabCredential.Create(
            "test",
            "token",
            BaseUrl.Create("https://gitlab.com").ValueOrThrow());
        BranchProtection protection = new("main", RejectDirectPushes: true, RejectForcePushes: true, RejectDeletion: true);
        RepositoryEligibilityEvaluator sut = CreateSut(
            resolver: new StubCredentialResolver(credential),
            providerFactory: new StubProviderFactory(
                Result<BranchProtection>.Ok(protection),
                canPushResult: Result<bool>.Fail(new Error("Provider.Error", "Upstream error"))));

        // Act
        await sut.EvaluateAndStoreAsync(repo, CancellationToken.None);

        // Assert
        repo.Eligibility.ShouldBeOfType<RepositoryEligibility.Unreachable>();
    }

    [Fact]
    public async Task WhenGitLabCanPushThrows_SetsEligibilityToUnreachable()
    {
        // Arrange
        MonitoredRepository repo = CreateRepo();
        GitLabCredential credential = GitLabCredential.Create(
            "test",
            "token",
            BaseUrl.Create("https://gitlab.com").ValueOrThrow());
        RepositoryEligibilityEvaluator sut = CreateSut(
            resolver: new StubCredentialResolver(credential),
            providerFactory: new ThrowingCanPushProviderFactory());

        // Act
        await sut.EvaluateAndStoreAsync(repo, CancellationToken.None);

        // Assert
        repo.Eligibility.ShouldBeOfType<RepositoryEligibility.Unreachable>();
    }

    [Fact]
    public async Task WhenGitLabCredentialCoversRepo_AndCanPushReturnsTrue_AndBranchProtectionPasses_SetsEligibilityToEligible()
    {
        // Arrange
        MonitoredRepository repo = CreateRepo();
        GitLabCredential credential = GitLabCredential.Create(
            "test",
            "token",
            BaseUrl.Create("https://gitlab.com").ValueOrThrow());
        BranchProtection protection = new("main", RejectDirectPushes: true, RejectForcePushes: true, RejectDeletion: true);
        StubProviderFactory factory = new(
            Result<BranchProtection>.Ok(protection),
            canPushResult: Result<bool>.Ok(true));
        RepositoryEligibilityEvaluator sut = CreateSut(
            resolver: new StubCredentialResolver(credential),
            providerFactory: factory);

        // Act
        await sut.EvaluateAndStoreAsync(repo, CancellationToken.None);

        // Assert
        repo.Eligibility.ShouldBeOfType<RepositoryEligibility.Eligible>();
        factory.CreatedProvider.ShouldNotBeNull();
        factory.CreatedProvider!.BranchProtectionWasCalled.ShouldBeTrue();
    }

    [Fact]
    public async Task WhenCredentialCoversRepo_AndProviderReturnsDirectPushesViolation_SetsEligibilityToIneligible()
    {
        // Arrange
        MonitoredRepository repo = CreateRepo();
        BranchProtection protection = new("main", RejectDirectPushes: false, RejectForcePushes: true, RejectDeletion: true);
        GitHubCredential credential = GitHubCredential.Create(
            "test",
            "token",
            BaseUrl.Create("https://github.com").ValueOrThrow());
        RepositoryEligibilityEvaluator sut = CreateSut(
            resolver: new StubCredentialResolver(credential),
            providerFactory: new StubProviderFactory(Result<BranchProtection>.Ok(protection)));

        // Act
        await sut.EvaluateAndStoreAsync(repo, CancellationToken.None);

        // Assert
        RepositoryEligibility.Ineligible ineligible = repo.Eligibility.ShouldBeOfType<RepositoryEligibility.Ineligible>();
        ineligible.Violations.ShouldContain(v => v.Rule == EligibilityViolation.AllowDirectPushesRule);
    }

    [Fact]
    public async Task WhenCredentialCoversRepo_AndProviderReturnsForcePushesViolation_SetsEligibilityToIneligible()
    {
        // Arrange
        MonitoredRepository repo = CreateRepo();
        BranchProtection protection = new("main", RejectDirectPushes: true, RejectForcePushes: false, RejectDeletion: true);
        GitHubCredential credential = GitHubCredential.Create(
            "test",
            "token",
            BaseUrl.Create("https://github.com").ValueOrThrow());
        RepositoryEligibilityEvaluator sut = CreateSut(
            resolver: new StubCredentialResolver(credential),
            providerFactory: new StubProviderFactory(Result<BranchProtection>.Ok(protection)));

        // Act
        await sut.EvaluateAndStoreAsync(repo, CancellationToken.None);

        // Assert
        RepositoryEligibility.Ineligible ineligible = repo.Eligibility.ShouldBeOfType<RepositoryEligibility.Ineligible>();
        ineligible.Violations.ShouldContain(v => v.Rule == EligibilityViolation.AllowForcePushesRule);
    }

    [Fact]
    public async Task WhenCredentialCoversRepo_AndProviderReturnsDeletionViolation_SetsEligibilityToIneligible()
    {
        // Arrange
        MonitoredRepository repo = CreateRepo();
        BranchProtection protection = new("main", RejectDirectPushes: true, RejectForcePushes: true, RejectDeletion: false);
        GitHubCredential credential = GitHubCredential.Create(
            "test",
            "token",
            BaseUrl.Create("https://github.com").ValueOrThrow());
        RepositoryEligibilityEvaluator sut = CreateSut(
            resolver: new StubCredentialResolver(credential),
            providerFactory: new StubProviderFactory(Result<BranchProtection>.Ok(protection)));

        // Act
        await sut.EvaluateAndStoreAsync(repo, CancellationToken.None);

        // Assert
        RepositoryEligibility.Ineligible ineligible = repo.Eligibility.ShouldBeOfType<RepositoryEligibility.Ineligible>();
        ineligible.Violations.ShouldContain(v => v.Rule == EligibilityViolation.AllowDeletionRule);
    }

    [Fact]
    public async Task WhenCredentialCoversRepo_AndProviderFails_SetsEligibilityToUnreachable()
    {
        // Arrange
        MonitoredRepository repo = CreateRepo();
        GitHubCredential credential = GitHubCredential.Create(
            "test",
            "token",
            BaseUrl.Create("https://github.com").ValueOrThrow());
        RepositoryEligibilityEvaluator sut = CreateSut(
            resolver: new StubCredentialResolver(credential),
            providerFactory: new StubProviderFactory(
                Result<BranchProtection>.Fail(new Error("Provider.Error", "Unreachable"))));

        // Act
        await sut.EvaluateAndStoreAsync(repo, CancellationToken.None);

        // Assert
        repo.Eligibility.ShouldBeOfType<RepositoryEligibility.Unreachable>();
    }

    [Fact]
    public async Task WhenGetBranchProtectionThrows_SetsEligibilityToUnreachable()
    {
        // Arrange
        MonitoredRepository repo = CreateRepo();
        GitHubCredential credential = GitHubCredential.Create(
            "test",
            "token",
            BaseUrl.Create("https://github.com").ValueOrThrow());
        RepositoryEligibilityEvaluator sut = CreateSut(
            resolver: new StubCredentialResolver(credential),
            providerFactory: new ThrowingBranchProtectionProviderFactory());

        // Act
        await sut.EvaluateAndStoreAsync(repo, CancellationToken.None);

        // Assert
        repo.Eligibility.ShouldBeOfType<RepositoryEligibility.Unreachable>();
    }

    // Returns null — simulates no credential covering the repository
    private sealed class NullCredentialResolver : ICredentialResolver
    {
        public Task<Credential?> ResolveAsync(
            string host,
            RepositorySlug slug,
            CancellationToken cancellationToken)
            => Task.FromResult<Credential?>(null);
    }

    private sealed class StubCredentialResolver(Credential credential) : ICredentialResolver
    {
        public Task<Credential?> ResolveAsync(
            string host,
            RepositorySlug slug,
            CancellationToken cancellationToken)
            => Task.FromResult<Credential?>(credential);
    }

    private sealed class NullProviderFactory : IIssueProviderFactory
    {
        public IIssueProvider CreateProvider(Credential credential, string token) =>
            throw new InvalidOperationException("Provider factory must not be called when no credential covers the repo.");
    }

    private sealed class TrackingProviderFactory : IIssueProviderFactory
    {
        public bool WasInvoked { get; private set; }

        public IIssueProvider CreateProvider(Credential credential, string token)
        {
            WasInvoked = true;
            return new NullProvider();
        }

        private sealed class NullProvider : IIssueProvider
        {
            public Task<Result<BranchProtection>> GetBranchProtectionAsync(
                RepositorySlug slug,
                CancellationToken cancellationToken)
                => Task.FromResult(Result<BranchProtection>.Ok(
                    new BranchProtection("main", true, true, true)));

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
                => Task.FromResult(Result<PullRequestStatus>.Ok(new PullRequestStatus(false, false)));

            public Task<Result<ReviewFeedback>> GetReviewFeedbackAsync(
                RepositorySlug slug,
                string pullRequestUrl,
                DateTimeOffset since,
                CancellationToken cancellationToken)
                => Task.FromResult(Result<ReviewFeedback>.Ok(new ReviewFeedback([])));

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

            public Task<Result<MergeRequestByBranch>> GetMergeRequestByBranchAsync(
                RepositorySlug slug,
                string branchName,
                CancellationToken cancellationToken)
                => Task.FromResult(
                    Result<MergeRequestByBranch>.Ok(new MergeRequestByBranch(MergeRequestPresence.None, null)));

            public Task<Result<LatestBranchCommit>> GetLatestBranchCommitAsync(
                RepositorySlug slug,
                string branchName,
                CancellationToken cancellationToken)
                => Task.FromResult(
                    Result<LatestBranchCommit>.Fail(new Error("Provider.NoCommit", "No commit found")));

            public Task<Result<bool>> CanPushAsync(
                RepositorySlug slug,
                CancellationToken cancellationToken)
                => Task.FromResult(Result<bool>.Ok(true));
        }
    }

    private sealed class StubProviderFactory(
        Result<BranchProtection> branchProtectionResult,
        Result<bool>? canPushResult = null) : IIssueProviderFactory
    {
        private readonly Result<bool> _canPushResult = canPushResult ?? Result<bool>.Ok(true);

        public StubProvider? CreatedProvider { get; private set; }

        public IIssueProvider CreateProvider(Credential credential, string token)
        {
            StubProvider provider = new(branchProtectionResult, _canPushResult);
            CreatedProvider = provider;
            return provider;
        }
    }

    private sealed class ThrowingBranchProtectionProviderFactory : IIssueProviderFactory
    {
        public IIssueProvider CreateProvider(Credential credential, string token) =>
            new ThrowingBranchProtectionProvider();
    }

    private sealed class StubProvider(
        Result<BranchProtection> branchProtectionResult,
        Result<bool>? canPushResult = null) : IIssueProvider
    {
        private readonly Result<bool> _canPushResult = canPushResult ?? Result<bool>.Ok(true);

        public bool BranchProtectionWasCalled { get; private set; }

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
        {
            BranchProtectionWasCalled = true;
            return Task.FromResult(branchProtectionResult);
        }

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

        public Task<Result<MergeRequestByBranch>> GetMergeRequestByBranchAsync(
            RepositorySlug slug,
            string branchName,
            CancellationToken cancellationToken)
            => Task.FromResult(
                Result<MergeRequestByBranch>.Ok(new MergeRequestByBranch(MergeRequestPresence.None, null)));

        public Task<Result<LatestBranchCommit>> GetLatestBranchCommitAsync(
            RepositorySlug slug,
            string branchName,
            CancellationToken cancellationToken)
            => Task.FromResult(
                Result<LatestBranchCommit>.Fail(new Error("Provider.NoCommit", "No commit found")));

        public Task<Result<bool>> CanPushAsync(
            RepositorySlug slug,
            CancellationToken cancellationToken)
            => Task.FromResult(_canPushResult);
    }

    private sealed class ThrowingBranchProtectionProvider : IIssueProvider
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
            => throw new HttpRequestException("Connection refused");

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

        public Task<Result<MergeRequestByBranch>> GetMergeRequestByBranchAsync(
            RepositorySlug slug,
            string branchName,
            CancellationToken cancellationToken)
            => Task.FromResult(
                Result<MergeRequestByBranch>.Ok(new MergeRequestByBranch(MergeRequestPresence.None, null)));

        public Task<Result<LatestBranchCommit>> GetLatestBranchCommitAsync(
            RepositorySlug slug,
            string branchName,
            CancellationToken cancellationToken)
            => Task.FromResult(
                Result<LatestBranchCommit>.Fail(new Error("Provider.NoCommit", "No commit found")));

        public Task<Result<bool>> CanPushAsync(
            RepositorySlug slug,
            CancellationToken cancellationToken)
            => Task.FromResult(Result<bool>.Ok(true));
    }

    private sealed class ThrowingCanPushProviderFactory : IIssueProviderFactory
    {
        public IIssueProvider CreateProvider(Credential credential, string token) =>
            new ThrowingCanPushProvider();

        private sealed class ThrowingCanPushProvider : IIssueProvider
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
                => Task.FromResult(Result<BranchProtection>.Ok(
                    new BranchProtection("main", true, true, true)));

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

            public Task<Result<MergeRequestByBranch>> GetMergeRequestByBranchAsync(
                RepositorySlug slug,
                string branchName,
                CancellationToken cancellationToken)
                => Task.FromResult(
                    Result<MergeRequestByBranch>.Ok(new MergeRequestByBranch(MergeRequestPresence.None, null)));

            public Task<Result<LatestBranchCommit>> GetLatestBranchCommitAsync(
                RepositorySlug slug,
                string branchName,
                CancellationToken cancellationToken)
                => Task.FromResult(
                    Result<LatestBranchCommit>.Fail(new Error("Provider.NoCommit", "No commit found")));

            public Task<Result<bool>> CanPushAsync(
                RepositorySlug slug,
                CancellationToken cancellationToken)
                => throw new HttpRequestException("Connection refused");
        }
    }

    // Always returns Granted — default for tests that don't exercise the probe outcome
    private sealed class GrantedWriteProber : IGitHubWriteProber
    {
        public Task<Result<WritePermissionProbeResult>> ProbeWriteAccessAsync(
            Uri apiBaseUrl,
            RepositorySlug slug,
            string token,
            CancellationToken cancellationToken)
            => Task.FromResult(Result<WritePermissionProbeResult>.Ok(new WritePermissionProbeResult.Granted()));
    }

    // Returns Missing with the given permission — simulates insufficient write access
    private sealed class MissingWriteProber(WritePermission permission) : IGitHubWriteProber
    {
        public Task<Result<WritePermissionProbeResult>> ProbeWriteAccessAsync(
            Uri apiBaseUrl,
            RepositorySlug slug,
            string token,
            CancellationToken cancellationToken)
            => Task.FromResult(
                Result<WritePermissionProbeResult>.Ok(new WritePermissionProbeResult.Missing(permission)));
    }

    // Returns transport failure — simulates unreachable GitHub API
    private sealed class FailingWriteProber : IGitHubWriteProber
    {
        public Task<Result<WritePermissionProbeResult>> ProbeWriteAccessAsync(
            Uri apiBaseUrl,
            RepositorySlug slug,
            string token,
            CancellationToken cancellationToken)
            => Task.FromResult(
                Result<WritePermissionProbeResult>.Fail(new Error("GitHub.Unreachable", "Transport failure")));
    }
}
