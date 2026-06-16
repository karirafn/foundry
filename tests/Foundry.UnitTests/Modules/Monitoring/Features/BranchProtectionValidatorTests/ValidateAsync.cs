using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Features;
using Foundry.Shared;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.BranchProtectionValidatorTests;

public sealed class ValidateAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly IBranchProtectionValidator _sut;

    private StubIssueProvider _stubProvider = new(
        Result<BranchProtection>.Ok(new BranchProtection("main", true, true, true)));

    public ValidateAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new FoundryDbContext(options);
        _dbContext.Database.EnsureCreated();
        _sut = new BranchProtectionValidator(
            _dbContext,
            new StubProviderFactory(() => _stubProvider));
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private async Task<(MonitoredRepositoryId, RepositorySlug)> SeedRepoAsync(
        string slug = "owner/repo",
        string? token = "ghp_test_token")
    {
        GitHubAccount account = GitHubAccount.Create("my-org", token, new Uri("https://github.com"));
        _dbContext.Set<Account>().Add(account);

        Result<RepositorySlug> slugResult = RepositorySlug.Create(slug);
        RepositorySlug repositorySlug = ((Result<RepositorySlug>.Success)slugResult).Value;

        MonitoredRepository repo = MonitoredRepository.Create(repositorySlug, account.Id, null);
        _dbContext.Set<MonitoredRepository>().Add(repo);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        return (repo.Id, repositorySlug);
    }

    [Fact]
    public async Task WhenAllProtectionsEnabled_ReturnsEmptyViolations()
    {
        // Arrange
        _stubProvider = new StubIssueProvider(
            Result<BranchProtection>.Ok(new BranchProtection(
                DefaultBranch: "main",
                RejectDirectPushes: true,
                RejectForcePushes: true,
                RejectDeletion: true)));

        (MonitoredRepositoryId repoId, _) = await SeedRepoAsync();

        // Act
        Result<IReadOnlyList<EligibilityViolationInfo>> result = await _sut.ValidateAsync(
            repoId,
            TestContext.Current.CancellationToken);

        // Assert
        Result<IReadOnlyList<EligibilityViolationInfo>>.Success success =
            result.ShouldBeOfType<Result<IReadOnlyList<EligibilityViolationInfo>>.Success>();
        success.Value.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenDirectPushesAllowed_ReturnsDirectPushViolation()
    {
        // Arrange
        _stubProvider = new StubIssueProvider(
            Result<BranchProtection>.Ok(new BranchProtection(
                DefaultBranch: "main",
                RejectDirectPushes: false,
                RejectForcePushes: true,
                RejectDeletion: true)));

        (MonitoredRepositoryId repoId, _) = await SeedRepoAsync();

        // Act
        Result<IReadOnlyList<EligibilityViolationInfo>> result = await _sut.ValidateAsync(
            repoId,
            TestContext.Current.CancellationToken);

        // Assert
        Result<IReadOnlyList<EligibilityViolationInfo>>.Success success =
            result.ShouldBeOfType<Result<IReadOnlyList<EligibilityViolationInfo>>.Success>();
        success.Value.ShouldContain(v => v.Rule == "branch-protection:allow-direct-pushes");
    }

    [Fact]
    public async Task WhenForcePushesAllowed_ReturnsForcePushViolation()
    {
        // Arrange
        _stubProvider = new StubIssueProvider(
            Result<BranchProtection>.Ok(new BranchProtection(
                DefaultBranch: "main",
                RejectDirectPushes: true,
                RejectForcePushes: false,
                RejectDeletion: true)));

        (MonitoredRepositoryId repoId, _) = await SeedRepoAsync();

        // Act
        Result<IReadOnlyList<EligibilityViolationInfo>> result = await _sut.ValidateAsync(
            repoId,
            TestContext.Current.CancellationToken);

        // Assert
        Result<IReadOnlyList<EligibilityViolationInfo>>.Success success =
            result.ShouldBeOfType<Result<IReadOnlyList<EligibilityViolationInfo>>.Success>();
        success.Value.ShouldContain(v => v.Rule == "branch-protection:allow-force-pushes");
    }

    [Fact]
    public async Task WhenDeletionAllowed_ReturnsDeletionViolation()
    {
        // Arrange
        _stubProvider = new StubIssueProvider(
            Result<BranchProtection>.Ok(new BranchProtection(
                DefaultBranch: "main",
                RejectDirectPushes: true,
                RejectForcePushes: true,
                RejectDeletion: false)));

        (MonitoredRepositoryId repoId, _) = await SeedRepoAsync();

        // Act
        Result<IReadOnlyList<EligibilityViolationInfo>> result = await _sut.ValidateAsync(
            repoId,
            TestContext.Current.CancellationToken);

        // Assert
        Result<IReadOnlyList<EligibilityViolationInfo>>.Success success =
            result.ShouldBeOfType<Result<IReadOnlyList<EligibilityViolationInfo>>.Success>();
        success.Value.ShouldContain(v => v.Rule == "branch-protection:allow-deletion");
    }

    [Fact]
    public async Task WhenAllProtectionsDisabled_ReturnsThreeViolations()
    {
        // Arrange
        _stubProvider = new StubIssueProvider(
            Result<BranchProtection>.Ok(new BranchProtection(
                DefaultBranch: "main",
                RejectDirectPushes: false,
                RejectForcePushes: false,
                RejectDeletion: false)));

        (MonitoredRepositoryId repoId, _) = await SeedRepoAsync();

        // Act
        Result<IReadOnlyList<EligibilityViolationInfo>> result = await _sut.ValidateAsync(
            repoId,
            TestContext.Current.CancellationToken);

        // Assert
        Result<IReadOnlyList<EligibilityViolationInfo>>.Success success =
            result.ShouldBeOfType<Result<IReadOnlyList<EligibilityViolationInfo>>.Success>();
        success.Value.Count.ShouldBe(3);
    }

    [Fact]
    public async Task WhenBranchProtectionUnreachable_ReturnsUnreachableViolation()
    {
        // Arrange
        _stubProvider = new StubIssueProvider(
            Result<BranchProtection>.Fail(new Error("Provider.Error", "Connection timed out.")));

        (MonitoredRepositoryId repoId, _) = await SeedRepoAsync();

        // Act
        Result<IReadOnlyList<EligibilityViolationInfo>> result = await _sut.ValidateAsync(
            repoId,
            TestContext.Current.CancellationToken);

        // Assert
        Result<IReadOnlyList<EligibilityViolationInfo>>.Success success =
            result.ShouldBeOfType<Result<IReadOnlyList<EligibilityViolationInfo>>.Success>();
        success.Value.ShouldContain(v => v.Rule == "branch-protection:unreachable");
    }

    [Fact]
    public async Task WhenRepositoryNotFound_ReturnsFailure()
    {
        // Arrange
        MonitoredRepositoryId unknownId = MonitoredRepositoryId.New();

        // Act
        Result<IReadOnlyList<EligibilityViolationInfo>> result = await _sut.ValidateAsync(
            unknownId,
            TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeOfType<Result<IReadOnlyList<EligibilityViolationInfo>>.Failure>();
    }

    [Fact]
    public async Task WhenAccountHasNoToken_ReturnsFailure()
    {
        // Arrange
        (MonitoredRepositoryId repoId, _) = await SeedRepoAsync(token: null);

        // Act
        Result<IReadOnlyList<EligibilityViolationInfo>> result = await _sut.ValidateAsync(
            repoId,
            TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeOfType<Result<IReadOnlyList<EligibilityViolationInfo>>.Failure>();
    }

    private sealed class StubProviderFactory(Func<StubIssueProvider> providerFactory) : IIssueProviderFactory
    {
        public IIssueProvider CreateProvider(Account account, string token)
        {
            return providerFactory();
        }
    }

    private sealed class StubIssueProvider(Result<BranchProtection> branchProtectionResult) : IIssueProvider
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
            return Task.FromResult(branchProtectionResult);
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

        public Task<Result<string>> GetPullRequestByBranchAsync(
            RepositorySlug slug,
            string branchName,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(Result<string>.Ok(string.Empty));
        }
    }
}
