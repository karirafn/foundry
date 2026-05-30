using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain;
using Foundry.Modules.Issues.Features;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Features.WorkerRunCompletedHandlerTests;

public sealed class HandleAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly IIntegrationEventHandler<WorkerRunCompleted> _sut;

    private static IssueAuthor ValidAuthor =>
        ((Result<IssueAuthor>.Success)IssueAuthor.Create("octocat")).Value;

    private static ProviderUrl ValidUrl =>
        ((Result<ProviderUrl>.Success)ProviderUrl.Create("https://github.com/owner/repo/issues/1")).Value;

    public HandleAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new FoundryDbContext(options);
        _dbContext.Database.EnsureCreated();
        _sut = new WorkerRunCompletedHandler(
            _dbContext,
            NullLogger<WorkerRunCompletedHandler>.Instance);
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private InProgressIssue SeedInProgressIssue(MonitoredRepositoryId repositoryId)
    {
        DetectedIssue detected = DetectedIssue.Detect(
            repositoryId,
            issueNumber: 1,
            title: "Issue 1",
            body: "Body",
            author: ValidAuthor,
            url: ValidUrl,
            labels: [],
            detectedAt: DateTimeOffset.UtcNow);
        QueuedIssue queued = QueuedIssue.FromDetected(detected);
        Guid workerRunId = Guid.NewGuid();
        InProgressIssue inProgress = queued.Claim(workerRunId);
        _dbContext.Set<Issue>().Add(inProgress);
        _dbContext.SaveChanges();
        _dbContext.ChangeTracker.Clear();
        return inProgress;
    }

    [Fact]
    public async Task WhenInProgressIssueWithPrUrl_TransitionsToReviewIssue()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        InProgressIssue inProgress = SeedInProgressIssue(repositoryId);

        WorkerRunCompleted @event = new(
            WorkerRunId: inProgress.WorkerRunId,
            IssueId: inProgress.Id.Value,
            BranchName: "feat/issue-1-fix",
            PullRequestUrl: "https://github.com/owner/repo/pull/10");

        // Act
        await _sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dbContext.ChangeTracker.Clear();
        Issue? issue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == repositoryId,
                TestContext.Current.CancellationToken);
        ReviewIssue review = issue.ShouldBeOfType<ReviewIssue>();
        review.ShouldSatisfyAllConditions(
            () => review.WorkerRunId.ShouldBe(inProgress.WorkerRunId),
            () => review.BranchName.ShouldBe("feat/issue-1-fix"),
            () => review.PullRequestUrl.ShouldBe("https://github.com/owner/repo/pull/10"));
    }

    [Fact]
    public async Task WhenInProgressIssueWithoutPrUrl_TransitionsToUnchangedIssue()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        InProgressIssue inProgress = SeedInProgressIssue(repositoryId);

        WorkerRunCompleted @event = new(
            WorkerRunId: inProgress.WorkerRunId,
            IssueId: inProgress.Id.Value,
            BranchName: null,
            PullRequestUrl: null);

        // Act
        await _sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dbContext.ChangeTracker.Clear();
        Issue? issue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == repositoryId,
                TestContext.Current.CancellationToken);
        UnchangedIssue unchanged = issue.ShouldBeOfType<UnchangedIssue>();
        unchanged.WorkerRunId.ShouldBe(inProgress.WorkerRunId);
    }

    private QueuedIssue SeedQueuedIssue(MonitoredRepositoryId repositoryId)
    {
        DetectedIssue detected = DetectedIssue.Detect(
            repositoryId,
            issueNumber: 2,
            title: "Issue 2",
            body: "Body",
            author: ValidAuthor,
            url: ValidUrl,
            labels: [],
            detectedAt: DateTimeOffset.UtcNow);
        QueuedIssue queued = QueuedIssue.FromDetected(detected);
        _dbContext.Set<Issue>().Add(queued);
        _dbContext.SaveChanges();
        _dbContext.ChangeTracker.Clear();
        return queued;
    }

    [Fact]
    public async Task WhenIssueNotInProgress_SilentlyIgnores()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        QueuedIssue queued = SeedQueuedIssue(repositoryId);

        WorkerRunCompleted @event = new(
            WorkerRunId: Guid.NewGuid(),
            IssueId: queued.Id.Value,
            BranchName: "feat/something",
            PullRequestUrl: "https://github.com/owner/repo/pull/5");

        // Act
        Task act = _sut.HandleAsync(@event, CancellationToken.None);

        // Assert — does not throw; issue remains in original state
        await Should.NotThrowAsync(act);
        _dbContext.ChangeTracker.Clear();
        Issue? issue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == repositoryId,
                TestContext.Current.CancellationToken);
        issue.ShouldBeOfType<QueuedIssue>();
    }
}
