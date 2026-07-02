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

public sealed class HasBranchCommitsAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly IPostExitProviderQueries _sut;

    private StubIssueProvider _stubProvider = new();

    public HasBranchCommitsAsync()
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
            new StubProviderFactory(() => _stubProvider));
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private async Task<MonitoredRepositoryId> SeedRepoAsync(string? token = "ghp_test_token")
    {
        GitHubAccount account = GitHubAccount.Create("my-org", token, BaseUrl.Create("https://github.com").ValueOrThrow());
        _dbContext.Set<Account>().Add(account);

        RepositorySlug slug = RepositorySlug.Create("owner/repo").ValueOrThrow();

        MonitoredRepository repo = MonitoredRepository.Create(slug, account.Id, "github.com", null);
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
        Result<bool> result = await _sut.HasBranchCommitsAsync(
            nonExistentId,
            "feat/my-branch",
            TestContext.Current.CancellationToken);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Result<bool>.Failure failure = result.ShouldBeOfType<Result<bool>.Failure>();
        failure.Error.Code.ShouldBe("PostExitProviderQueries.RepositoryNotFound");
    }

    [Fact]
    public async Task WhenBranchHasCommits_ReturnsTrue()
    {
        // Arrange
        MonitoredRepositoryId repoId = await SeedRepoAsync();
        _stubProvider = new StubIssueProvider(hasBranchCommitsResult: Result<bool>.Ok(true));

        // Act
        Result<bool> result = await _sut.HasBranchCommitsAsync(
            repoId,
            "feat/my-branch",
            TestContext.Current.CancellationToken);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<bool>.Success success = result.ShouldBeOfType<Result<bool>.Success>();
        success.Value.ShouldBeTrue();
    }

    [Fact]
    public async Task WhenBranchHasNoNewCommits_ReturnsFalse()
    {
        // Arrange
        MonitoredRepositoryId repoId = await SeedRepoAsync();
        _stubProvider = new StubIssueProvider(hasBranchCommitsResult: Result<bool>.Ok(false));

        // Act
        Result<bool> result = await _sut.HasBranchCommitsAsync(
            repoId,
            "feat/my-branch",
            TestContext.Current.CancellationToken);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<bool>.Success success = result.ShouldBeOfType<Result<bool>.Success>();
        success.Value.ShouldBeFalse();
    }

    private sealed class StubProviderFactory(Func<StubIssueProvider> providerFactory) : IIssueProviderFactory
    {
        public IIssueProvider CreateProvider(Account account, string token) => providerFactory();
    }

    private sealed class StubIssueProvider(
        Result<bool>? hasBranchCommitsResult = null) : IIssueProvider
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
            return Task.FromResult(Result<BranchProtection>.Ok(new BranchProtection("main", false, false, false)));
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
            return Task.FromResult(hasBranchCommitsResult ?? Result<bool>.Ok(false));
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
                Result<LatestBranchCommit>.Fail(new Error("Provider.NoCommit", "No commit found")));
        }
    }
}
