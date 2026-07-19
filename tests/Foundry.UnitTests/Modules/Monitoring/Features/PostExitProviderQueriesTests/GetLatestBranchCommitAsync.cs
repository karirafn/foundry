using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.PostExitProviderQueriesTests;

public sealed class GetLatestBranchCommitAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly IPostExitProviderQueries _sut;

    private StubIssueProvider _stubProvider = new();

    public GetLatestBranchCommitAsync()
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
        Result<LatestBranchCommit> result = await _sut.GetLatestBranchCommitAsync(
            nonExistentId,
            "feat/my-branch",
            TestContext.Current.CancellationToken);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Result<LatestBranchCommit>.Failure failure = result.ShouldBeOfType<Result<LatestBranchCommit>.Failure>();
        failure.Error.Code.ShouldBe("PostExitProviderQueries.RepositoryNotFound");
    }

    [Fact]
    public async Task WhenProviderReturnsCommit_ReturnsCommitDetails()
    {
        // Arrange
        MonitoredRepositoryId repoId = await SeedRepoAsync();
        LatestBranchCommit commit = new("abc1234", "feat: add something");
        _stubProvider = new StubIssueProvider(
            getLatestBranchCommitResult: Result<LatestBranchCommit>.Ok(commit));

        // Act
        Result<LatestBranchCommit> result = await _sut.GetLatestBranchCommitAsync(
            repoId,
            "feat/my-branch",
            TestContext.Current.CancellationToken);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<LatestBranchCommit>.Success success = result.ShouldBeOfType<Result<LatestBranchCommit>.Success>();
        success.Value.ShouldSatisfyAllConditions(
            () => success.Value.Sha.ShouldBe("abc1234"),
            () => success.Value.Message.ShouldBe("feat: add something"));
    }

    [Fact]
    public async Task WhenProviderIsUnreachable_ReturnsFailure()
    {
        // Arrange
        MonitoredRepositoryId repoId = await SeedRepoAsync();
        Error providerError = new("Provider.Unreachable", "Provider is unreachable");
        _stubProvider = new StubIssueProvider(
            getLatestBranchCommitResult: Result<LatestBranchCommit>.Fail(providerError));

        // Act
        Result<LatestBranchCommit> result = await _sut.GetLatestBranchCommitAsync(
            repoId,
            "feat/my-branch",
            TestContext.Current.CancellationToken);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Result<LatestBranchCommit>.Failure failure = result.ShouldBeOfType<Result<LatestBranchCommit>.Failure>();
        failure.Error.Code.ShouldBe("Provider.Unreachable");
    }

    private sealed class StubProviderFactory(Func<StubIssueProvider> providerFactory) : IIssueProviderFactory
    {
        public IIssueProvider CreateProvider(Credential credential, string token) => providerFactory();
    }

    private sealed class StubIssueProvider(
        Result<LatestBranchCommit>? getLatestBranchCommitResult = null) : IIssueProvider
    {
        public Task<Result<IReadOnlyList<ProviderIssue>>> GetIssuesAsync(
            RepositorySlug slug,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(Result<IReadOnlyList<ProviderIssue>>.Ok([]));
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
            return Task.FromResult(Result<ReviewFeedback>.Ok(new ReviewFeedback([])));
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

        public Task<Result<bool>> HasBranchCommitsAsync(
            RepositorySlug slug,
            string branchName,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(Result<bool>.Ok(false));
        }

        public Task<Result<MergeRequestByBranch>> GetMergeRequestByBranchAsync(
            RepositorySlug slug,
            string branchName,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(
                Result<MergeRequestByBranch>.Ok(new MergeRequestByBranch(MergeRequestPresence.None, null)));
        }

        public Task<Result<LatestBranchCommit>> GetLatestBranchCommitAsync(
            RepositorySlug slug,
            string branchName,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(
                getLatestBranchCommitResult
                ?? Result<LatestBranchCommit>.Fail(new Error("Provider.NoCommit", "No commit found")));
        }
    }
}
