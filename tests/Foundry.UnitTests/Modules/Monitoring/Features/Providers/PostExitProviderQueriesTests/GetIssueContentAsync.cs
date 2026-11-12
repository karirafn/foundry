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

public sealed class GetIssueContentAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly IssueContentProvider _provider = new();
    private readonly IIssueContentQueries _sut;

    public GetIssueContentAsync()
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
        GitHubCredential credential = GitHubCredential.Create("my-org", "ghp_test_token", BaseUrl.Create("https://github.com").ValueOrThrow());
        credential.SetNamespaces([Namespace.Create("owner").ValueOrThrow()]);
        _dbContext.Set<Credential>().Add(credential);

        MonitoredRepository repo = MonitoredRepository.Create(
            RepositorySlug.Create("owner/repo").ValueOrThrow(), "github.com", null).ValueOrThrow();
        _dbContext.Set<MonitoredRepository>().Add(repo);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        return repo.Id;
    }

    [Fact]
    public async Task WhenTheProviderReadsTheIssue_ReturnsItsTitleBodyAndComments()
    {
        // Arrange
        DateTimeOffset createdAt = new(2026, 6, 1, 10, 0, 0, TimeSpan.Zero);
        _provider.Content = Result<ProviderIssueContent>.Ok(new ProviderIssueContent(
            "Add retry",
            "Retry webhook delivery.",
            [
                new ProviderComment(
                    ProviderCommentId.Create(11).ValueOrThrow(), "Which webhooks?", "bob", false, false, createdAt,
                    null, null, CommentOrigin.Conversation, false),
                new ProviderComment(
                    null, "All of them.", "alice", false, false, createdAt.AddHours(1),
                    null, null, CommentOrigin.Conversation, false),
            ]));

        // Act
        Result<IssueContent> result = await _sut.GetIssueContentAsync(
            await SeedRepoAsync(), 7, TestContext.Current.CancellationToken);

        // Assert
        IssueContent content = result.ShouldBeOfType<Result<IssueContent>.Success>().Value;
        content.ShouldSatisfyAllConditions(
            () => content.Title.ShouldBe("Add retry"),
            () => content.Body.ShouldBe("Retry webhook delivery."),
            () => content.Comments.ShouldBe(
            [
                new IssueContentComment(11, "bob", "Which webhooks?", createdAt),
                new IssueContentComment(null, "alice", "All of them.", createdAt.AddHours(1)),
            ]),
            () => _provider.RequestedIssueNumbers.ShouldBe([7]));
    }

    [Fact]
    public async Task WhenTheProviderReadFails_ReturnsTheFailure()
    {
        // Arrange
        Error error = new("GitHub.Unexpected", "boom");
        _provider.Content = Result<ProviderIssueContent>.Fail(error);

        // Act
        Result<IssueContent> result = await _sut.GetIssueContentAsync(
            await SeedRepoAsync(), 7, TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeOfType<Result<IssueContent>.Failure>().Error.ShouldBe(error);
    }

    [Fact]
    public async Task WhenTheRepositoryIsUnknown_FailsWithoutCallingTheProvider()
    {
        // Act
        Result<IssueContent> result = await _sut.GetIssueContentAsync(
            MonitoredRepositoryId.New(), 7, TestContext.Current.CancellationToken);

        // Assert
        result.ShouldSatisfyAllConditions(
            () => result.ShouldBeOfType<Result<IssueContent>.Failure>(),
            () => _provider.RequestedIssueNumbers.ShouldBeEmpty());
    }

    // Only the issue-content read is exercised; the rest are never reached by GetIssueContentAsync.
    private sealed class IssueContentProvider : IIssueProvider
    {
        private readonly List<int> _requestedIssueNumbers = [];

        public Result<ProviderIssueContent> Content { get; set; } =
            Result<ProviderIssueContent>.Ok(new ProviderIssueContent(string.Empty, string.Empty, []));

        public IReadOnlyList<int> RequestedIssueNumbers => _requestedIssueNumbers;

        public Task<Result<ProviderIssueContent>> GetIssueContentAsync(RepositorySlug slug, int issueNumber, CancellationToken cancellationToken)
        {
            _requestedIssueNumbers.Add(issueNumber);
            return Task.FromResult(Content);
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

        public Task<Result<MergeRequestByBranch>> GetMergeRequestByBranchAsync(RepositorySlug slug, string branchName, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Result<BranchCommitSummary>> GetBranchCommitSummaryAsync(RepositorySlug slug, string branchName, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Result<bool>> CanPushAsync(RepositorySlug slug, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Result<ProviderChecks>> GetChecksAsync(RepositorySlug slug, string pullRequestUrl, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
