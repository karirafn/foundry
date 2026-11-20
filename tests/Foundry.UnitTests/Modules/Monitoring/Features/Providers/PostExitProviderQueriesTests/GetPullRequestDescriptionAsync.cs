using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.CredentialResolution;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Modules.Monitoring.Features.Providers.Checks;
using Foundry.Modules.Monitoring.Features.Providers.Comments;
using Foundry.Modules.Monitoring.Features.Providers.Feedback;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.UnitTests.Fakes.Monitoring;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

using ProviderChecks = Foundry.Modules.Monitoring.Features.Providers.Checks.Checks;

namespace Foundry.UnitTests.Modules.Monitoring.Features.Providers.PostExitProviderQueriesTests;

public sealed class GetPullRequestDescriptionAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly PullRequestBodyProvider _provider = new();
    private readonly IIssueContentQueries _sut;

    public GetPullRequestDescriptionAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _dbContext = new FoundryDbContext(new DbContextOptionsBuilder<FoundryDbContext>().UseSqlite(_connection).Options);
        _dbContext.Database.EnsureCreated();

        _sut = new PostExitProviderQueries(
            _dbContext,
            new SingleIssueProviderFactory(_provider),
            new CredentialResolver(_dbContext, NullLogger<CredentialResolver>.Instance));
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private async Task<MonitoredRepositoryId> SeedRepoAsync()
    {
        GitHubCredential credential = GitHubCredential.Create(AccountName.Create("my-org").ValueOrThrow(), new IssuedToken("ghp_test_token", new TokenExpiry.NotChecked()), BaseUrl.Create("https://github.com").ValueOrThrow());
        credential.SetNamespaces([Namespace.Create("owner").ValueOrThrow()]);
        _dbContext.Set<Credential>().Add(credential);

        MonitoredRepository repo = MonitoredRepository.Create(
            RepositorySlug.Create("owner/repo").ValueOrThrow(), "github.com", null).ValueOrThrow();
        _dbContext.Set<MonitoredRepository>().Add(repo);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        return repo.Id;
    }

    private const string PullRequestUrl = "https://github.com/owner/repo/pull/12";

    [Fact]
    public async Task WhenTheProviderReadsTheBody_ReturnsIt()
    {
        // Arrange
        _provider.Body = Result<string>.Ok("## Post-merge steps");

        // Act
        Result<string> result = await _sut.GetPullRequestDescriptionAsync(
            await SeedRepoAsync(), PullRequestUrl, TestContext.Current.CancellationToken);

        // Assert
        result.ShouldSatisfyAllConditions(
            () => result.ValueOrThrow().ShouldBe("## Post-merge steps"),
            () => _provider.RequestedPullRequestUrls.ShouldBe([PullRequestUrl]));
    }

    [Fact]
    public async Task WhenTheProviderReadFails_ReturnsTheFailure()
    {
        // Arrange
        Error error = new("GitHub.Unexpected", "boom");
        _provider.Body = Result<string>.Fail(error);

        // Act
        Result<string> result = await _sut.GetPullRequestDescriptionAsync(
            await SeedRepoAsync(), PullRequestUrl, TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeOfType<Result<string>.Failure>().Error.ShouldBe(error);
    }

    [Fact]
    public async Task WhenTheRepositoryIsUnknown_FailsWithoutCallingTheProvider()
    {
        // Act
        Result<string> result = await _sut.GetPullRequestDescriptionAsync(
            MonitoredRepositoryId.New(), PullRequestUrl, TestContext.Current.CancellationToken);

        // Assert
        result.ShouldSatisfyAllConditions(
            () => result.ShouldBeOfType<Result<string>.Failure>(),
            () => _provider.RequestedPullRequestUrls.ShouldBeEmpty());
    }

    // Only the pull request body read is exercised; the rest are never reached by GetPullRequestDescriptionAsync.
    private sealed class PullRequestBodyProvider : IIssueProvider
    {
        private readonly List<string> _requestedPullRequestUrls = [];

        public Result<string> Body { get; set; } = Result<string>.Ok(string.Empty);

        public IReadOnlyList<string> RequestedPullRequestUrls => _requestedPullRequestUrls;

        public Task<Result<string>> GetPullRequestBodyAsync(RepositorySlug slug, string pullRequestUrl, CancellationToken cancellationToken)
        {
            _requestedPullRequestUrls.Add(pullRequestUrl);
            return Task.FromResult(Body);
        }

        public Task<Result<IssueListing>> GetIssuesAsync(RepositorySlug slug, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Result<IReadOnlyList<int>>> GetDependenciesAsync(RepositorySlug slug, int issueNumber, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Result<bool>> IsIssueClosedAsync(RepositorySlug slug, int issueNumber, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Result<PullRequestStatus>> GetPullRequestStatusAsync(RepositorySlug slug, string pullRequestUrl, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Result<ReviewFeedback>> GetReviewFeedbackAsync(RepositorySlug slug, string pullRequestUrl, DateTimeOffset since, PostedCommentIds postedComments, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Result<BranchProtection>> GetBranchProtectionAsync(RepositorySlug slug, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Result> ApplyBranchRulesAsync(RepositorySlug slug, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Result<bool>> CreateBranchAsync(RepositorySlug slug, string branchName, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Result<bool>> DeleteBranchAsync(RepositorySlug slug, string branchName, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Result<MergeRequestByBranch>> GetMergeRequestByBranchAsync(RepositorySlug slug, string branchName, CommitSha? publishedHead, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Result<BranchCommitSummary>> GetBranchCommitSummaryAsync(RepositorySlug slug, string branchName, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Result<bool>> CanPushAsync(RepositorySlug slug, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Result<ProviderChecks>> GetChecksAsync(RepositorySlug slug, string pullRequestUrl, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
