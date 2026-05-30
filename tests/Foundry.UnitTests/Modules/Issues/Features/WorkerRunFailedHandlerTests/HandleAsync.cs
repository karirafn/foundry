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

namespace Foundry.UnitTests.Modules.Issues.Features.WorkerRunFailedHandlerTests;

public sealed class HandleAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly IIntegrationEventHandler<WorkerRunFailed> _sut;

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
        _sut = new WorkerRunFailedHandler(
            _dbContext,
            NullLogger<WorkerRunFailedHandler>.Instance);
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
    public async Task WhenInProgressIssue_TransitionsToFailedIssue()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        InProgressIssue inProgress = SeedInProgressIssue(repositoryId);

        WorkerRunFailed @event = new(
            WorkerRunId: inProgress.WorkerRunId,
            IssueId: inProgress.Id.Value,
            ReasonDescription: "Non-zero exit code: 1");

        // Act
        await _sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dbContext.ChangeTracker.Clear();
        Issue? issue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == repositoryId,
                TestContext.Current.CancellationToken);
        FailedIssue failed = issue.ShouldBeOfType<FailedIssue>();
        failed.ShouldSatisfyAllConditions(
            () => failed.WorkerRunId.ShouldBe(inProgress.WorkerRunId),
            () => failed.FailureReason.ShouldBe("Non-zero exit code: 1"));
    }

    [Fact]
    public async Task WhenIssueNotInProgress_SilentlyIgnores()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        QueuedIssue queued = SeedQueuedIssue(repositoryId);

        WorkerRunFailed @event = new(
            WorkerRunId: Guid.NewGuid(),
            IssueId: queued.Id.Value,
            ReasonDescription: "Something went wrong");

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
