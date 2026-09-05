using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.CredentialResolution;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Modules.Monitoring.Features.Providers.Feedback;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.Providers.PostExitProviderQueriesTests;

public sealed class GetBranchCommitSummaryAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly IPostExitProviderQueries _sut;

    private StubIssueProvider _stubProvider = new();

    public GetBranchCommitSummaryAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new FoundryDbContext(options);
        _dbContext.Database.EnsureCreated();

        _sut = new PostExitProviderQueries(
            _dbContext,
            new StubProviderFactory(() => _stubProvider),
            new CredentialResolver(_dbContext));
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private async Task<MonitoredRepositoryId> SeedRepoAsync(string? token = "ghp_test_token")
    {
        GitHubCredential credential = GitHubCredential.Create("my-org", token, BaseUrl.Create("https://github.com").ValueOrThrow());
        credential.SetNamespaces([Namespace.Create("owner").ValueOrThrow()]);
        _dbContext.Set<Credential>().Add(credential);

        RepositorySlug slug = RepositorySlug.Create("owner/repo").ValueOrThrow();

        MonitoredRepository repo = MonitoredRepository.Create(slug, "github.com", null);
        _dbContext.Set<MonitoredRepository>().Add(repo);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        return repo.Id;
    }

    [Fact]
    public async Task WhenRepositoryNotFound_ReturnsFailure()
    {
        // Arrange
        MonitoredRepositoryId nonExistentId = MonitoredRepositoryId.New();

        // Act
        Result<BranchCommitSummary> result = await _sut.GetBranchCommitSummaryAsync(
            nonExistentId,
            "feat/my-branch",
            TestContext.Current.CancellationToken);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Result<BranchCommitSummary>.Failure failure = result.ShouldBeOfType<Result<BranchCommitSummary>.Failure>();
        failure.Error.Code.ShouldBe("PostExitProviderQueries.RepositoryNotFound");
    }

    [Fact]
    public async Task WhenProviderReturnsSummary_ReturnsSummaryDetails()
    {
        // Arrange
        MonitoredRepositoryId repoId = await SeedRepoAsync();
        BranchCommitSummary summary = new(CommitCount: 3, LatestSha: "abc1234ef56789012345678901234567890abcde");
        _stubProvider = new StubIssueProvider(
            getBranchCommitSummaryResult: Result<BranchCommitSummary>.Ok(summary));

        // Act
        Result<BranchCommitSummary> result = await _sut.GetBranchCommitSummaryAsync(
            repoId,
            "feat/my-branch",
            TestContext.Current.CancellationToken);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<BranchCommitSummary>.Success success = result.ShouldBeOfType<Result<BranchCommitSummary>.Success>();
        success.Value.ShouldSatisfyAllConditions(
            () => success.Value.CommitCount.ShouldBe(3),
            () => success.Value.LatestSha.ShouldBe("abc1234ef56789012345678901234567890abcde"));
    }

    [Fact]
    public async Task WhenProviderReturnsZeroCommits_ReturnsSummaryWithNullSha()
    {
        // Arrange
        MonitoredRepositoryId repoId = await SeedRepoAsync();
        BranchCommitSummary summary = new(CommitCount: 0, LatestSha: null);
        _stubProvider = new StubIssueProvider(
            getBranchCommitSummaryResult: Result<BranchCommitSummary>.Ok(summary));

        // Act
        Result<BranchCommitSummary> result = await _sut.GetBranchCommitSummaryAsync(
            repoId,
            "feat/my-branch",
            TestContext.Current.CancellationToken);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<BranchCommitSummary>.Success success = result.ShouldBeOfType<Result<BranchCommitSummary>.Success>();
        success.Value.ShouldSatisfyAllConditions(
            () => success.Value.CommitCount.ShouldBe(0),
            () => success.Value.LatestSha.ShouldBeNull());
    }

    [Fact]
    public async Task WhenProviderIsUnreachable_ReturnsFailure()
    {
        // Arrange
        MonitoredRepositoryId repoId = await SeedRepoAsync();
        Error providerError = new("Provider.Unreachable", "Provider is unreachable");
        _stubProvider = new StubIssueProvider(
            getBranchCommitSummaryResult: Result<BranchCommitSummary>.Fail(providerError));

        // Act
        Result<BranchCommitSummary> result = await _sut.GetBranchCommitSummaryAsync(
            repoId,
            "feat/my-branch",
            TestContext.Current.CancellationToken);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Result<BranchCommitSummary>.Failure failure = result.ShouldBeOfType<Result<BranchCommitSummary>.Failure>();
        failure.Error.Code.ShouldBe("Provider.Unreachable");
    }

    private sealed class StubProviderFactory(Func<StubIssueProvider> providerFactory) : IIssueProviderFactory
    {
        public IIssueProvider CreateProvider(Credential credential, string token) => providerFactory();
    }

    private sealed class StubIssueProvider(
        Result<BranchCommitSummary>? getBranchCommitSummaryResult = null) : IIssueProvider
    {
        public Task<Result<IssueListing>> GetIssuesAsync(
            RepositorySlug slug,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(Result<IssueListing>.Ok(new IssueListing([], IsComplete: true)));
        }

        public Task<Result<IReadOnlyList<int>>> GetDependenciesAsync(
            RepositorySlug slug,
            int issueNumber,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(Result<IReadOnlyList<int>>.Ok([]));
        }

        public Task<Result<bool>> IsIssueClosedAsync(
            RepositorySlug slug,
            int issueNumber,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(Result<bool>.Ok(false));
        }

        public Task<Result<PullRequestStatus>> GetPullRequestStatusAsync(
            RepositorySlug slug,
            string pullRequestUrl,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(
                Result<PullRequestStatus>.Ok(new PullRequestStatus(IsClosed: false, IsMerged: false)));
        }

        public Task<Result<ReviewFeedback>> GetReviewFeedbackAsync(
            RepositorySlug slug,
            string pullRequestUrl,
            DateTimeOffset since,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(Result<ReviewFeedback>.Ok(new ReviewFeedback([], OmittedCommentCount: 0, NewestCommentAt: null)));
        }

        public Task<Result<BranchProtection>> GetBranchProtectionAsync(
            RepositorySlug slug,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(
                Result<BranchProtection>.Ok(new BranchProtection("main", false, false, false)));
        }

        public Task<Result<bool>> CreateBranchAsync(
            RepositorySlug slug,
            string branchName,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(Result<bool>.Ok(true));
        }

        public Task<Result<MergeRequestByBranch>> GetMergeRequestByBranchAsync(
            RepositorySlug slug,
            string branchName,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(
                Result<MergeRequestByBranch>.Ok(new MergeRequestByBranch(MergeRequestPresence.None, null)));
        }

        public Task<Result<BranchCommitSummary>> GetBranchCommitSummaryAsync(
            RepositorySlug slug,
            string branchName,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(
                getBranchCommitSummaryResult
                ?? Result<BranchCommitSummary>.Fail(new Error("Provider.NoCommit", "No commit found")));
        }

        public Task<Result<bool>> CanPushAsync(
            RepositorySlug slug,
            CancellationToken cancellationToken)
            => Task.FromResult(Result<bool>.Ok(true));
    }
}
