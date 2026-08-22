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

public sealed class EvaluateBranchRulesAndStoreAsync
{
    private static MonitoredRepository CreateRepo(
        string slug = "owner/repo",
        WriteProbeVerdict? verdict = null)
    {
        RepositorySlug repositorySlug = RepositorySlug.Create(slug).ValueOrThrow();
        MonitoredRepository repo = MonitoredRepository.Create(repositorySlug, "github.com", pollInterval: null);

        if (verdict is not null)
        {
            repo.SetWriteProbeVerdict(verdict);
        }

        return repo;
    }

    private static RepositoryEligibilityEvaluator CreateSut(
        ICredentialResolver? resolver = null,
        IIssueProviderFactory? providerFactory = null,
        IGitHubWriteProber? writeProber = null)
    {
        return new RepositoryEligibilityEvaluator(
            resolver ?? new NullCredentialResolver(),
            providerFactory ?? new NullProviderFactory(),
            writeProber ?? new NeverCalledWriteProber(),
            NullLogger<RepositoryEligibilityEvaluator>.Instance);
    }

    [Fact]
    public async Task WhenNoCredentialCoversRepo_SetsEligibilityToIneligibleWithNoCredentialViolation()
    {
        // Arrange
        MonitoredRepository repo = CreateRepo("myorg/repo", new WriteProbeVerdict.Granted());
        RepositoryEligibilityEvaluator sut = CreateSut(resolver: new NullCredentialResolver());

        // Act
        await sut.EvaluateBranchRulesAndStoreAsync(repo, CancellationToken.None);

        // Assert
        RepositoryEligibility.Ineligible ineligible = repo.Eligibility.ShouldBeOfType<RepositoryEligibility.Ineligible>();
        ineligible.Violations.ShouldHaveSingleItem();
        ineligible.Violations[0].Rule.ShouldBe("no-credential:myorg");
    }

    [Fact]
    public async Task WhenNoCredentialCoversRepo_DoesNotInvokeProviderFactory()
    {
        // Arrange
        MonitoredRepository repo = CreateRepo(verdict: new WriteProbeVerdict.Granted());
        TrackingProviderFactory trackingFactory = new();
        RepositoryEligibilityEvaluator sut = CreateSut(
            resolver: new NullCredentialResolver(),
            providerFactory: trackingFactory);

        // Act
        await sut.EvaluateBranchRulesAndStoreAsync(repo, CancellationToken.None);

        // Assert
        trackingFactory.WasInvoked.ShouldBeFalse();
    }

    [Fact]
    public async Task WhenVerdictIsDenied_SetsEligibilityToIneligibleWithCannotPushViolation()
    {
        // Arrange
        MonitoredRepository repo = CreateRepo("owner/repo", new WriteProbeVerdict.Denied());
        GitHubCredential credential = GitHubCredential.Create(
            "test",
            "token",
            BaseUrl.Create("https://github.com").ValueOrThrow());
        TrackingProviderFactory trackingFactory = new();
        RepositoryEligibilityEvaluator sut = CreateSut(
            resolver: new StubCredentialResolver(credential),
            providerFactory: trackingFactory);

        // Act
        await sut.EvaluateBranchRulesAndStoreAsync(repo, CancellationToken.None);

        // Assert
        RepositoryEligibility.Ineligible ineligible = repo.Eligibility.ShouldBeOfType<RepositoryEligibility.Ineligible>();
        ineligible.Violations.ShouldHaveSingleItem();
        ineligible.Violations[0].Rule.ShouldBe("cannot-push:owner/repo");
    }

    [Fact]
    public async Task WhenVerdictIsDenied_DoesNotRunBranchRulesGet()
    {
        // Arrange — Denied short-circuits before branch-rules GET
        MonitoredRepository repo = CreateRepo(verdict: new WriteProbeVerdict.Denied());
        GitHubCredential credential = GitHubCredential.Create(
            "test",
            "token",
            BaseUrl.Create("https://github.com").ValueOrThrow());
        TrackingProviderFactory trackingFactory = new();
        RepositoryEligibilityEvaluator sut = CreateSut(
            resolver: new StubCredentialResolver(credential),
            providerFactory: trackingFactory);

        // Act
        await sut.EvaluateBranchRulesAndStoreAsync(repo, CancellationToken.None);

        // Assert
        trackingFactory.WasInvoked.ShouldBeFalse();
    }

    [Fact]
    public async Task WhenVerdictIsUnknown_SetsEligibilityToUnreachable()
    {
        // Arrange — Unknown means never probed; pit-of-success: do not treat as eligible
        MonitoredRepository repo = CreateRepo(verdict: new WriteProbeVerdict.Unknown());
        GitHubCredential credential = GitHubCredential.Create(
            "test",
            "token",
            BaseUrl.Create("https://github.com").ValueOrThrow());
        RepositoryEligibilityEvaluator sut = CreateSut(
            resolver: new StubCredentialResolver(credential),
            providerFactory: new NullProviderFactory());

        // Act
        await sut.EvaluateBranchRulesAndStoreAsync(repo, CancellationToken.None);

        // Assert
        repo.Eligibility.ShouldBeOfType<RepositoryEligibility.Unreachable>();
    }

    [Fact]
    public async Task WhenVerdictIsUnknown_DoesNotRunBranchRulesGet()
    {
        // Arrange — Unknown short-circuits without invoking the provider
        MonitoredRepository repo = CreateRepo(verdict: new WriteProbeVerdict.Unknown());
        GitHubCredential credential = GitHubCredential.Create(
            "test",
            "token",
            BaseUrl.Create("https://github.com").ValueOrThrow());
        TrackingProviderFactory trackingFactory = new();
        RepositoryEligibilityEvaluator sut = CreateSut(
            resolver: new StubCredentialResolver(credential),
            providerFactory: trackingFactory);

        // Act
        await sut.EvaluateBranchRulesAndStoreAsync(repo, CancellationToken.None);

        // Assert
        trackingFactory.WasInvoked.ShouldBeFalse();
    }

    [Fact]
    public async Task WhenVerdictIsGranted_AndBranchProtectionPasses_SetsEligibilityToEligible()
    {
        // Arrange — auto-heal: stored Granted + passing ruleset → Eligible without any probe (ADR-0013)
        MonitoredRepository repo = CreateRepo(verdict: new WriteProbeVerdict.Granted());
        BranchProtection protection = new("main", RejectDirectPushes: true, RejectForcePushes: true, RejectDeletion: true);
        GitHubCredential credential = GitHubCredential.Create(
            "test",
            "token",
            BaseUrl.Create("https://github.com").ValueOrThrow());
        RepositoryEligibilityEvaluator sut = CreateSut(
            resolver: new StubCredentialResolver(credential),
            providerFactory: new StubProviderFactory(Result<BranchProtection>.Ok(protection)));

        // Act
        await sut.EvaluateBranchRulesAndStoreAsync(repo, CancellationToken.None);

        // Assert
        repo.Eligibility.ShouldBeOfType<RepositoryEligibility.Eligible>();
    }

    [Fact]
    public async Task WhenVerdictIsGranted_AndBranchProtectionPasses_DoesNotRunWriteProbe()
    {
        // Arrange — EvaluateBranchRulesAndStoreAsync must not invoke IGitHubWriteProber
        MonitoredRepository repo = CreateRepo(verdict: new WriteProbeVerdict.Granted());
        BranchProtection protection = new("main", RejectDirectPushes: true, RejectForcePushes: true, RejectDeletion: true);
        GitHubCredential credential = GitHubCredential.Create(
            "test",
            "token",
            BaseUrl.Create("https://github.com").ValueOrThrow());

        // NeverCalledWriteProber throws if invoked — proves no write probe was issued
        RepositoryEligibilityEvaluator sut = CreateSut(
            resolver: new StubCredentialResolver(credential),
            providerFactory: new StubProviderFactory(Result<BranchProtection>.Ok(protection)),
            writeProber: new NeverCalledWriteProber());

        // Act
        await sut.EvaluateBranchRulesAndStoreAsync(repo, CancellationToken.None);

        // Assert
        repo.Eligibility.ShouldBeOfType<RepositoryEligibility.Eligible>();
    }

    [Fact]
    public async Task WhenVerdictIsGranted_AndBranchProtectionFails_SetsEligibilityToUnreachable()
    {
        // Arrange
        MonitoredRepository repo = CreateRepo(verdict: new WriteProbeVerdict.Granted());
        GitHubCredential credential = GitHubCredential.Create(
            "test",
            "token",
            BaseUrl.Create("https://github.com").ValueOrThrow());
        RepositoryEligibilityEvaluator sut = CreateSut(
            resolver: new StubCredentialResolver(credential),
            providerFactory: new StubProviderFactory(
                Result<BranchProtection>.Fail(new Error("Provider.Error", "Unreachable"))));

        // Act
        await sut.EvaluateBranchRulesAndStoreAsync(repo, CancellationToken.None);

        // Assert
        repo.Eligibility.ShouldBeOfType<RepositoryEligibility.Unreachable>();
    }

    [Fact]
    public async Task WhenVerdictIsGranted_AndBranchProtectionThrows_SetsEligibilityToUnreachable()
    {
        // Arrange
        MonitoredRepository repo = CreateRepo(verdict: new WriteProbeVerdict.Granted());
        GitHubCredential credential = GitHubCredential.Create(
            "test",
            "token",
            BaseUrl.Create("https://github.com").ValueOrThrow());
        RepositoryEligibilityEvaluator sut = CreateSut(
            resolver: new StubCredentialResolver(credential),
            providerFactory: new ThrowingBranchProtectionProviderFactory());

        // Act
        await sut.EvaluateBranchRulesAndStoreAsync(repo, CancellationToken.None);

        // Assert
        repo.Eligibility.ShouldBeOfType<RepositoryEligibility.Unreachable>();
    }

    [Fact]
    public async Task WhenVerdictIsGranted_AndDirectPushViolation_SetsEligibilityToIneligible()
    {
        // Arrange
        MonitoredRepository repo = CreateRepo(verdict: new WriteProbeVerdict.Granted());
        BranchProtection protection = new("main", RejectDirectPushes: false, RejectForcePushes: true, RejectDeletion: true);
        GitHubCredential credential = GitHubCredential.Create(
            "test",
            "token",
            BaseUrl.Create("https://github.com").ValueOrThrow());
        RepositoryEligibilityEvaluator sut = CreateSut(
            resolver: new StubCredentialResolver(credential),
            providerFactory: new StubProviderFactory(Result<BranchProtection>.Ok(protection)));

        // Act
        await sut.EvaluateBranchRulesAndStoreAsync(repo, CancellationToken.None);

        // Assert
        RepositoryEligibility.Ineligible ineligible = repo.Eligibility.ShouldBeOfType<RepositoryEligibility.Ineligible>();
        ineligible.Violations.ShouldContain(v => v.Rule == EligibilityViolation.AllowDirectPushesRule);
    }

    [Fact]
    public async Task WhenVerdictIsGranted_DoesNotAlterStoredVerdict()
    {
        // Arrange — cheap path must not overwrite the persisted verdict
        MonitoredRepository repo = CreateRepo(verdict: new WriteProbeVerdict.Granted());
        BranchProtection protection = new("main", RejectDirectPushes: true, RejectForcePushes: true, RejectDeletion: true);
        GitHubCredential credential = GitHubCredential.Create(
            "test",
            "token",
            BaseUrl.Create("https://github.com").ValueOrThrow());
        RepositoryEligibilityEvaluator sut = CreateSut(
            resolver: new StubCredentialResolver(credential),
            providerFactory: new StubProviderFactory(Result<BranchProtection>.Ok(protection)));

        // Act
        await sut.EvaluateBranchRulesAndStoreAsync(repo, CancellationToken.None);

        // Assert
        repo.WriteProbeVerdict.ShouldBeOfType<WriteProbeVerdict.Granted>();
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
            throw new InvalidOperationException("Provider factory must not be called when verdict short-circuits.");
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

            public Task<Result<MergeRequestByBranch>> GetMergeRequestByBranchAsync(
                RepositorySlug slug,
                string branchName,
                CancellationToken cancellationToken)
                => Task.FromResult(
                    Result<MergeRequestByBranch>.Ok(new MergeRequestByBranch(MergeRequestPresence.None, null)));

            public Task<Result<BranchCommitSummary>> GetBranchCommitSummaryAsync(
                RepositorySlug slug,
                string branchName,
                CancellationToken cancellationToken)
                => Task.FromResult(
                    Result<BranchCommitSummary>.Fail(new Error("Provider.NoCommit", "No commit found")));

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

        public IIssueProvider CreateProvider(Credential credential, string token) =>
            new StubProvider(branchProtectionResult, _canPushResult);
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

        public Task<Result<MergeRequestByBranch>> GetMergeRequestByBranchAsync(
            RepositorySlug slug,
            string branchName,
            CancellationToken cancellationToken)
            => Task.FromResult(
                Result<MergeRequestByBranch>.Ok(new MergeRequestByBranch(MergeRequestPresence.None, null)));

        public Task<Result<BranchCommitSummary>> GetBranchCommitSummaryAsync(
            RepositorySlug slug,
            string branchName,
            CancellationToken cancellationToken)
            => Task.FromResult(
                Result<BranchCommitSummary>.Fail(new Error("Provider.NoCommit", "No commit found")));

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

        public Task<Result<MergeRequestByBranch>> GetMergeRequestByBranchAsync(
            RepositorySlug slug,
            string branchName,
            CancellationToken cancellationToken)
            => Task.FromResult(
                Result<MergeRequestByBranch>.Ok(new MergeRequestByBranch(MergeRequestPresence.None, null)));

        public Task<Result<BranchCommitSummary>> GetBranchCommitSummaryAsync(
            RepositorySlug slug,
            string branchName,
            CancellationToken cancellationToken)
            => Task.FromResult(
                Result<BranchCommitSummary>.Fail(new Error("Provider.NoCommit", "No commit found")));

        public Task<Result<bool>> CanPushAsync(
            RepositorySlug slug,
            CancellationToken cancellationToken)
            => Task.FromResult(Result<bool>.Ok(true));
    }

    // Throws if invoked — proves no write probe was issued during the cheap evaluation path
    private sealed class NeverCalledWriteProber : IGitHubWriteProber
    {
        public Task<Result<WritePermissionProbeResult>> ProbeWriteAccessAsync(
            Uri apiBaseUrl,
            RepositorySlug slug,
            string token,
            CancellationToken cancellationToken)
            => throw new InvalidOperationException(
                "EvaluateBranchRulesAndStoreAsync must not invoke the write prober.");
    }
}
