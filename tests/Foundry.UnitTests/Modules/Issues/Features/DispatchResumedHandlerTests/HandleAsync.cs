using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain;
using Foundry.Modules.Issues.Features;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Features.DispatchResumedHandlerTests;

public sealed class HandleAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly CapturingDomainEventDispatcher _dispatcher;
    private readonly IIntegrationEventHandler<DispatchResumed> _sut;

    private static IssueAuthor ValidAuthor =>
        IssueAuthor.Create("octocat").ValueOrThrow();

    private static ProviderUrl ValidUrl =>
        ProviderUrl.Create("https://github.com/owner/repo/issues/1").ValueOrThrow();

    public HandleAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new FoundryDbContext(options);
        _dbContext.Database.EnsureCreated();
        _dispatcher = new CapturingDomainEventDispatcher();
        _sut = new DispatchResumedHandler(
            _dbContext,
            _dispatcher,
            NullLogger<DispatchResumedHandler>.Instance);
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private FailedIssue SeedFailedIssue(MonitoredRepositoryId repositoryId, string failureReason, int issueNumber = 1)
    {
        DetectedIssue detected = DetectedIssue.Detect(
            repositoryId,
            issueNumber: issueNumber,
            title: $"Issue {issueNumber}",
            body: "Body",
            author: ValidAuthor,
            url: ValidUrl,
            labels: [],
            detectedAt: DateTimeOffset.UtcNow);
        QueuedIssue queued = QueuedIssue.FromDetected(detected);
        InProgressIssue inProgress = queued.Claim(Guid.NewGuid());
        FailedIssue failed = inProgress.MarkFailed(
            inProgress.WorkerRunId,
            failureReason,
            DateTimeOffset.UtcNow,
            "generic_failure");
        _dbContext.Set<Issue>().Add(failed);
        _dbContext.SaveChanges();
        _dbContext.ChangeTracker.Clear();
        return failed;
    }

    private ContinuableFailedIssue SeedContinuableFailedIssue(
        MonitoredRepositoryId repositoryId,
        string failureReason,
        int issueNumber = 1)
    {
        DetectedIssue detected = DetectedIssue.Detect(
            repositoryId,
            issueNumber: issueNumber,
            title: $"Issue {issueNumber}",
            body: "Body",
            author: ValidAuthor,
            url: ValidUrl,
            labels: [],
            detectedAt: DateTimeOffset.UtcNow);
        QueuedIssue queued = QueuedIssue.FromDetected(detected);
        InProgressIssue inProgress = queued.Claim(Guid.NewGuid());
        ContinuableFailedIssue continuableFailed = inProgress.MarkContinuableFailed(
            inProgress.WorkerRunId,
            "feat/issue-branch",
            failureReason,
            "generic_failure",
            DateTimeOffset.UtcNow);
        _dbContext.Set<Issue>().Add(continuableFailed);
        _dbContext.SaveChanges();
        _dbContext.ChangeTracker.Clear();
        return continuableFailed;
    }

    [Fact]
    public async Task WhenFailedIssueIsUsageLimited_TransitionsToQueuedIssue()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedFailedIssue(repositoryId, WorkerRunFailed.UsageLimitedReason);

        DispatchResumed @event = new();

        // Act
        await _sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dbContext.ChangeTracker.Clear();
        Issue? issue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == repositoryId,
                TestContext.Current.CancellationToken);
        issue.ShouldBeOfType<QueuedIssue>();
    }

    [Fact]
    public async Task WhenContinuableFailedIssueIsUsageLimited_TransitionsToContinuationQueuedIssue()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedContinuableFailedIssue(repositoryId, WorkerRunFailed.UsageLimitedReason);

        DispatchResumed @event = new();

        // Act
        await _sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dbContext.ChangeTracker.Clear();
        Issue? issue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == repositoryId,
                TestContext.Current.CancellationToken);
        issue.ShouldBeOfType<ContinuationQueuedIssue>();
    }

    [Fact]
    public async Task WhenFailedIssueIsNotUsageLimited_RemainsAsFailed()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedFailedIssue(repositoryId, "Non-zero exit code: 1");

        DispatchResumed @event = new();

        // Act
        await _sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dbContext.ChangeTracker.Clear();
        Issue? issue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == repositoryId,
                TestContext.Current.CancellationToken);
        issue.ShouldBeOfType<FailedIssue>();
    }

    [Fact]
    public async Task WhenContinuableFailedIssueIsNotUsageLimited_RemainsAsContinuableFailed()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedContinuableFailedIssue(repositoryId, "Non-zero exit code: 1");

        DispatchResumed @event = new();

        // Act
        await _sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dbContext.ChangeTracker.Clear();
        Issue? issue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == repositoryId,
                TestContext.Current.CancellationToken);
        issue.ShouldBeOfType<ContinuableFailedIssue>();
    }

    [Fact]
    public async Task WhenNoFailedIssues_HandlesGracefully()
    {
        // Arrange
        DispatchResumed @event = new();

        // Act
        Task act = _sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        await Should.NotThrowAsync(act);
    }

    [Fact]
    public async Task WhenMultipleUsageLimitedFailedIssues_RetryAll()
    {
        // Arrange
        MonitoredRepositoryId repositoryId1 = MonitoredRepositoryId.New();
        MonitoredRepositoryId repositoryId2 = MonitoredRepositoryId.New();
        SeedFailedIssue(repositoryId1, WorkerRunFailed.UsageLimitedReason, issueNumber: 1);
        SeedFailedIssue(repositoryId2, WorkerRunFailed.UsageLimitedReason, issueNumber: 2);

        DispatchResumed @event = new();

        // Act
        await _sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dbContext.ChangeTracker.Clear();
        List<Issue> issues = await _dbContext.Set<Issue>()
            .ToListAsync(TestContext.Current.CancellationToken);
        issues.ShouldAllBe(i => i is QueuedIssue);
    }

    [Fact]
    public async Task WhenMixedFailedIssues_OnlyUsageLimitedAreRetried()
    {
        // Arrange
        MonitoredRepositoryId usageLimitedRepo = MonitoredRepositoryId.New();
        MonitoredRepositoryId nonUsageLimitedRepo = MonitoredRepositoryId.New();
        SeedFailedIssue(usageLimitedRepo, WorkerRunFailed.UsageLimitedReason, issueNumber: 1);
        SeedFailedIssue(nonUsageLimitedRepo, "Non-zero exit code: 1", issueNumber: 2);

        DispatchResumed @event = new();

        // Act
        await _sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dbContext.ChangeTracker.Clear();
        Issue? usageLimitedIssue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == usageLimitedRepo,
                TestContext.Current.CancellationToken);
        Issue? nonUsageLimitedIssue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == nonUsageLimitedRepo,
                TestContext.Current.CancellationToken);
        usageLimitedIssue.ShouldBeOfType<QueuedIssue>();
        nonUsageLimitedIssue.ShouldBeOfType<FailedIssue>();
    }
}
