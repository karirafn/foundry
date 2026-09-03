using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Features;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared.Infrastructure;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Features.IssuesModuleTests;

public sealed class GetReviewIssuesAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly IIssueQueries _sut;

    public GetReviewIssuesAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new FoundryDbContext(options);
        _dbContext.Database.EnsureCreated();
        _sut = new IssueQueries(_dbContext, new NullRepositorySlugQueries(), new NullRepositoryEligibilityQuery(), new NullWorkerRunQueries());
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private async Task<ReviewIssue> CreateAndPersistReviewIssueAsync(
        MonitoredRepositoryId repositoryId,
        int issueNumber,
        string pullRequestUrl)
    {
        DetectedIssue detected = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(issueNumber)
            .WithTitle($"Issue {issueNumber}")
            .Detected();

        _dbContext.Set<Issue>().Add(detected);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        FreshQueuedIssue queued = detected.Enqueue();
        await _dbContext.TransitionAsync(detected, queued, new NullDomainEventDispatcher(), TestContext.Current.CancellationToken);

        InProgressIssue inProgress = queued.Claim(WorkerRunId.New());
        await _dbContext.TransitionAsync(queued, inProgress, new NullDomainEventDispatcher(), TestContext.Current.CancellationToken);

        ReviewIssue review = inProgress.MarkInReview($"feat/issue-{issueNumber}", pullRequestUrl, DateTimeOffset.UtcNow);
        await _dbContext.TransitionAsync(inProgress, review, new NullDomainEventDispatcher(), TestContext.Current.CancellationToken);

        return review;
    }

    [Fact]
    public async Task WhenNoReviewIssuesExist_ReturnsEmptyList()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();

        // Act
        IReadOnlyList<ReviewIssueInfo> result = await _sut.GetReviewIssuesAsync(
            repositoryId,
            TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenReviewIssuesExistForRepository_ReturnsIssueNumberAndPullRequestUrl()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        const string pullRequestUrl = "https://github.com/owner/repo/pull/42";

        await CreateAndPersistReviewIssueAsync(repositoryId, issueNumber: 10, pullRequestUrl);

        // Act
        IReadOnlyList<ReviewIssueInfo> result = await _sut.GetReviewIssuesAsync(
            repositoryId,
            TestContext.Current.CancellationToken);

        // Assert
        ReviewIssueInfo info = result.ShouldHaveSingleItem();
        info.ShouldSatisfyAllConditions(
            () => info.IssueNumber.ShouldBe(10),
            () => info.PullRequestUrl.ShouldBe(pullRequestUrl));
    }

    [Fact]
    public async Task WhenReviewIssueExists_ReturnsFeedbackCutoffAt()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        const string pullRequestUrl = "https://github.com/owner/repo/pull/42";

        ReviewIssue review = await CreateAndPersistReviewIssueAsync(repositoryId, issueNumber: 10, pullRequestUrl);

        // Act
        IReadOnlyList<ReviewIssueInfo> result = await _sut.GetReviewIssuesAsync(
            repositoryId,
            TestContext.Current.CancellationToken);

        // Assert
        ReviewIssueInfo info = result.ShouldHaveSingleItem();
        info.FeedbackCutoffAt.ShouldBe(review.FeedbackCutoffAt);
    }

    [Fact]
    public async Task WhenReviewIssuesExistForDifferentRepository_ReturnsOnlyMatchingRepository()
    {
        // Arrange
        MonitoredRepositoryId targetRepo = MonitoredRepositoryId.New();
        MonitoredRepositoryId otherRepo = MonitoredRepositoryId.New();

        await CreateAndPersistReviewIssueAsync(targetRepo, issueNumber: 10, "https://github.com/owner/repo/pull/10");
        await CreateAndPersistReviewIssueAsync(otherRepo, issueNumber: 20, "https://github.com/owner/repo/pull/20");

        // Act
        IReadOnlyList<ReviewIssueInfo> result = await _sut.GetReviewIssuesAsync(
            targetRepo,
            TestContext.Current.CancellationToken);

        // Assert
        ReviewIssueInfo info = result.ShouldHaveSingleItem();
        info.IssueNumber.ShouldBe(10);
    }

    [Fact]
    public async Task WhenNonReviewIssuesExist_DoesNotIncludeNonReviewIssues()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();

        DetectedIssue detected = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(5)
            .WithTitle("Detected Issue")
            .Detected();

        _dbContext.Set<Issue>().Add(detected);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        IReadOnlyList<ReviewIssueInfo> result = await _sut.GetReviewIssuesAsync(
            repositoryId,
            TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeEmpty();
    }
}
