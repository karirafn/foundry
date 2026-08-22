using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Issues.Features;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Features.IssuesModuleTests;

public sealed class GetDispatchCandidateIssueNumbersAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly IIssueQueries _sut;

    private static readonly DateTimeOffset Now = new(2026, 5, 27, 12, 0, 0, TimeSpan.Zero);

    private static IssueAuthor ValidAuthor =>
        IssueAuthor.Create("octocat").ValueOrThrow();

    private static ProviderUrl ValidUrl =>
        ProviderUrl.Create("https://github.com/owner/repo/issues/1").ValueOrThrow();

    public GetDispatchCandidateIssueNumbersAsync()
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

    private DetectedIssue SeedDetectedIssue(MonitoredRepositoryId repositoryId, int issueNumber)
    {
        DetectedIssue detected = DetectedIssue.Detect(
            repositoryId,
            issueNumber: issueNumber,
            title: "Issue title",
            body: "Body",
            author: ValidAuthor,
            url: ValidUrl,
            labels: [],
            detectedAt: Now);
        _dbContext.Set<Issue>().Add(detected);
        _dbContext.SaveChanges();
        return detected;
    }

    private FreshQueuedIssue SeedFreshQueuedIssue(MonitoredRepositoryId repositoryId, int issueNumber)
    {
        DetectedIssue detected = DetectedIssue.Detect(
            repositoryId,
            issueNumber: issueNumber,
            title: "Issue title",
            body: "Body",
            author: ValidAuthor,
            url: ValidUrl,
            labels: [],
            detectedAt: Now);
        FreshQueuedIssue queued = FreshQueuedIssue.FromDetected(detected);
        _dbContext.Set<Issue>().Add(queued);
        _dbContext.SaveChanges();
        return queued;
    }

    private BlockedIssue SeedBlockedIssue(MonitoredRepositoryId repositoryId, int issueNumber)
    {
        DetectedIssue detected = DetectedIssue.Detect(
            repositoryId,
            issueNumber: issueNumber,
            title: "Issue title",
            body: "Body",
            author: ValidAuthor,
            url: ValidUrl,
            labels: [],
            detectedAt: Now);
        FreshQueuedIssue queued = FreshQueuedIssue.FromDetected(detected);
        BlockedIssue blocked = queued.Block([99]);
        _dbContext.Set<Issue>().Add(blocked);
        _dbContext.SaveChanges();
        return blocked;
    }

    private InProgressIssue SeedInProgressIssue(MonitoredRepositoryId repositoryId, int issueNumber)
    {
        DetectedIssue detected = DetectedIssue.Detect(
            repositoryId,
            issueNumber: issueNumber,
            title: "Issue title",
            body: "Body",
            author: ValidAuthor,
            url: ValidUrl,
            labels: [],
            detectedAt: Now);
        FreshQueuedIssue queued = FreshQueuedIssue.FromDetected(detected);
        InProgressIssue inProgress = queued.Claim(Guid.NewGuid());
        _dbContext.Set<Issue>().Add(inProgress);
        _dbContext.SaveChanges();
        return inProgress;
    }

    private CompletedIssue SeedCompletedIssue(MonitoredRepositoryId repositoryId, int issueNumber)
    {
        DetectedIssue detected = DetectedIssue.Detect(
            repositoryId,
            issueNumber: issueNumber,
            title: "Issue title",
            body: "Body",
            author: ValidAuthor,
            url: ValidUrl,
            labels: [],
            detectedAt: Now);
        FreshQueuedIssue queued = FreshQueuedIssue.FromDetected(detected);
        InProgressIssue inProgress = queued.Claim(Guid.NewGuid());
        ReviewIssue review = inProgress.MarkInReview(
            inProgress.WorkerRunId,
            "feat/1-fix",
            "https://github.com/owner/repo/pull/10",
            Now);
        CompletedIssue completed = review.Complete(Now);
        _dbContext.Set<Issue>().Add(completed);
        _dbContext.SaveChanges();
        return completed;
    }

    private RevisionQueuedIssue SeedRevisionQueuedIssue(MonitoredRepositoryId repositoryId, int issueNumber)
    {
        DetectedIssue detected = DetectedIssue.Detect(
            repositoryId,
            issueNumber: issueNumber,
            title: "Issue title",
            body: "Body",
            author: ValidAuthor,
            url: ValidUrl,
            labels: [],
            detectedAt: Now);
        FreshQueuedIssue queued = FreshQueuedIssue.FromDetected(detected);
        InProgressIssue inProgress = queued.Claim(Guid.NewGuid());
        ReviewIssue review = inProgress.MarkInReview(
            inProgress.WorkerRunId,
            "feat/1-fix",
            "https://github.com/owner/repo/pull/10",
            Now);
        RevisionQueuedIssue revisionQueued = review.Revise([new ReviewComment("Fix this")]);
        _dbContext.Set<Issue>().Add(revisionQueued);
        _dbContext.SaveChanges();
        return revisionQueued;
    }

    private ContinuationQueuedIssue SeedContinuationQueuedIssue(MonitoredRepositoryId repositoryId, int issueNumber)
    {
        DetectedIssue detected = DetectedIssue.Detect(
            repositoryId,
            issueNumber: issueNumber,
            title: "Issue title",
            body: "Body",
            author: ValidAuthor,
            url: ValidUrl,
            labels: [],
            detectedAt: Now);
        FreshQueuedIssue queued = FreshQueuedIssue.FromDetected(detected);
        InProgressIssue inProgress = queued.Claim(Guid.NewGuid());
        ContinuableFailedIssue continuableFailed = inProgress.MarkContinuableFailed(
            Guid.NewGuid(),
            "feat/1-fix",
            "Failure reason",
            "generic_failure",
            Now);
        ContinuationQueuedIssue continuationQueued = continuableFailed.Retry();
        _dbContext.Set<Issue>().Add(continuationQueued);
        _dbContext.SaveChanges();
        return continuationQueued;
    }

    [Fact]
    public async Task WhenFreshQueuedIssueExists_ReturnsItsNumber()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedFreshQueuedIssue(repositoryId, issueNumber: 2);

        // Act
        IReadOnlySet<int> result = await _sut.GetDispatchCandidateIssueNumbersAsync(
            repositoryId,
            TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBe([2], ignoreOrder: true);
    }

    [Fact]
    public async Task WhenDetectedIssueExists_ReturnsItsNumber()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedDetectedIssue(repositoryId, issueNumber: 1);

        // Act
        IReadOnlySet<int> result = await _sut.GetDispatchCandidateIssueNumbersAsync(
            repositoryId,
            TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBe([1], ignoreOrder: true);
    }

    [Fact]
    public async Task WhenBlockedIssueExists_ReturnsItsNumber()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedBlockedIssue(repositoryId, issueNumber: 3);

        // Act
        IReadOnlySet<int> result = await _sut.GetDispatchCandidateIssueNumbersAsync(
            repositoryId,
            TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBe([3], ignoreOrder: true);
    }

    [Fact]
    public async Task WhenNoCandidatesExist_ReturnsEmptySet()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();

        // Act
        IReadOnlySet<int> result = await _sut.GetDispatchCandidateIssueNumbersAsync(
            repositoryId,
            CancellationToken.None);

        // Assert
        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenInProgressIssueExists_DoesNotReturnItsNumber()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedInProgressIssue(repositoryId, issueNumber: 5);

        // Act
        IReadOnlySet<int> result = await _sut.GetDispatchCandidateIssueNumbersAsync(
            repositoryId,
            TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenCompletedIssueExists_DoesNotReturnItsNumber()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedCompletedIssue(repositoryId, issueNumber: 10);

        // Act
        IReadOnlySet<int> result = await _sut.GetDispatchCandidateIssueNumbersAsync(
            repositoryId,
            TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenRevisionQueuedIssueExists_DoesNotReturnItsNumber()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedRevisionQueuedIssue(repositoryId, issueNumber: 6);

        // Act
        IReadOnlySet<int> result = await _sut.GetDispatchCandidateIssueNumbersAsync(
            repositoryId,
            TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenContinuationQueuedIssueExists_DoesNotReturnItsNumber()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedContinuationQueuedIssue(repositoryId, issueNumber: 8);

        // Act
        IReadOnlySet<int> result = await _sut.GetDispatchCandidateIssueNumbersAsync(
            repositoryId,
            TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenMixOfCandidateAndNonCandidateIssuesExist_ReturnsOnlyCandidateNumbers()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedDetectedIssue(repositoryId, issueNumber: 1);
        SeedFreshQueuedIssue(repositoryId, issueNumber: 2);
        SeedBlockedIssue(repositoryId, issueNumber: 3);
        SeedInProgressIssue(repositoryId, issueNumber: 4);
        SeedCompletedIssue(repositoryId, issueNumber: 5);
        SeedRevisionQueuedIssue(repositoryId, issueNumber: 6);
        SeedContinuationQueuedIssue(repositoryId, issueNumber: 7);

        // Act
        IReadOnlySet<int> result = await _sut.GetDispatchCandidateIssueNumbersAsync(
            repositoryId,
            TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBe([1, 2, 3], ignoreOrder: true);
    }

    [Fact]
    public async Task WhenIssuesExistForDifferentRepository_ReturnsOnlyMatchingNumbers()
    {
        // Arrange
        MonitoredRepositoryId targetRepo = MonitoredRepositoryId.New();
        MonitoredRepositoryId otherRepo = MonitoredRepositoryId.New();
        SeedDetectedIssue(targetRepo, issueNumber: 10);
        SeedDetectedIssue(otherRepo, issueNumber: 20);

        // Act
        IReadOnlySet<int> result = await _sut.GetDispatchCandidateIssueNumbersAsync(
            targetRepo,
            TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBe([10], ignoreOrder: true);
    }
}
