using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Issues.Features.WorkerReactions;
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

namespace Foundry.UnitTests.Modules.Issues.Features.WorkerReactions.WorkerRunFailedHandlerTests;

public sealed class HandleAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly CapturingDomainEventDispatcher _dispatcher;
    private readonly IIntegrationEventHandler<WorkerRunFailed> _sut;

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
        _sut = new WorkerRunFailedHandler(
            _dbContext,
            _dispatcher,
            NullLogger<WorkerRunFailedHandler>.Instance);
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private InProgressIssue SeedInProgressIssue(MonitoredRepositoryId repositoryId)
    {
        InProgressIssue inProgress = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(1)
            .InProgress();
        _dbContext.Set<Issue>().Add(inProgress);
        _dbContext.SaveChanges();
        _dbContext.ChangeTracker.Clear();
        return inProgress;
    }

    private FreshQueuedIssue SeedQueuedIssue(MonitoredRepositoryId repositoryId)
    {
        FreshQueuedIssue queued = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(2)
            .FreshQueued();
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

    private RevisionInProgressIssue SeedRevisionInProgressIssue(MonitoredRepositoryId repositoryId)
    {
        RevisionInProgressIssue revisionInProgress = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(3)
            .WithBranchName("feat/issue-3")
            .WithPullRequestUrl("https://github.com/owner/repo/pull/3")
            .RevisionInProgress();
        _dbContext.Set<Issue>().Add(revisionInProgress);
        _dbContext.SaveChanges();
        _dbContext.ChangeTracker.Clear();
        return revisionInProgress;
    }

    [Fact]
    public async Task WhenRevisionInProgressFails_TransitionsToRevisionFailedIssue()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        RevisionInProgressIssue revisionInProgress = SeedRevisionInProgressIssue(repositoryId);

        WorkerRunFailed @event = new(
            WorkerRunId: revisionInProgress.WorkerRunId,
            IssueId: revisionInProgress.Id.Value,
            ReasonDescription: "Non-zero exit code: 2");

        // Act
        await _sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dbContext.ChangeTracker.Clear();
        Issue? issue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == repositoryId,
                TestContext.Current.CancellationToken);
        RevisionFailedIssue failed = issue.ShouldBeOfType<RevisionFailedIssue>();
        failed.ShouldSatisfyAllConditions(
            () => failed.WorkerRunId.ShouldBe(revisionInProgress.WorkerRunId),
            () => failed.FailureReason.ShouldBe("Non-zero exit code: 2"),
            () => failed.BranchName.ShouldBe(revisionInProgress.BranchName),
            () => failed.PullRequestUrl.ShouldBe(revisionInProgress.PullRequestUrl));
    }

    [Fact]
    public async Task WhenIssueNotInProgress_SilentlyIgnores()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        FreshQueuedIssue queued = SeedQueuedIssue(repositoryId);

        WorkerRunFailed @event = new(
            WorkerRunId: WorkerRunId.New(),
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
        issue.ShouldBeOfType<FreshQueuedIssue>();
    }

    [Fact]
    public async Task WhenInProgressIssueFails_DispatchesIssueFailedEvent()
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
        _dispatcher.DispatchedEvents
            .OfType<IssueFailed>()
            .ShouldHaveSingleItem();
    }

    [Fact]
    public async Task WhenInProgressIssueWithBranchName_TransitionsToContinuableFailedIssue()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        InProgressIssue inProgress = SeedInProgressIssue(repositoryId);

        WorkerRunFailed @event = new(
            WorkerRunId: inProgress.WorkerRunId,
            IssueId: inProgress.Id.Value,
            ReasonDescription: "Non-zero exit code: 1",
            BranchName: "feat/123-fix");

        // Act
        await _sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dbContext.ChangeTracker.Clear();
        Issue? issue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == repositoryId,
                TestContext.Current.CancellationToken);
        ContinuableFailedIssue continuableFailed = issue.ShouldBeOfType<ContinuableFailedIssue>();
        continuableFailed.ShouldSatisfyAllConditions(
            () => continuableFailed.BranchName.ShouldBe("feat/123-fix"),
            () => continuableFailed.FailureReason.ShouldBe("Non-zero exit code: 1"));
    }

    [Fact]
    public async Task WhenUsageLimitedEventReceived_FailureCategoryIsPersistedOnFailedIssue()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        InProgressIssue inProgress = SeedInProgressIssue(repositoryId);

        WorkerRunFailed @event = new(
            WorkerRunId: inProgress.WorkerRunId,
            IssueId: inProgress.Id.Value,
            ReasonDescription: WorkerRunFailed.UsageLimitedReason,
            Category: "usage_limited");

        // Act
        await _sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dbContext.ChangeTracker.Clear();
        Issue? issue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == repositoryId,
                TestContext.Current.CancellationToken);
        FailedIssue failed = issue.ShouldBeOfType<FailedIssue>();
        failed.FailureCategory.ShouldBe("usage_limited");
    }

    [Fact]
    public async Task WhenUsageLimitedContinuableFailureEventReceived_FailureCategoryIsPersistedOnContinuableFailedIssue()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        InProgressIssue inProgress = SeedInProgressIssue(repositoryId);

        WorkerRunFailed @event = new(
            WorkerRunId: inProgress.WorkerRunId,
            IssueId: inProgress.Id.Value,
            ReasonDescription: WorkerRunFailed.UsageLimitedReason,
            Category: "usage_limited",
            BranchName: "feat/123-fix");

        // Act
        await _sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dbContext.ChangeTracker.Clear();
        Issue? issue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == repositoryId,
                TestContext.Current.CancellationToken);
        ContinuableFailedIssue continuableFailed = issue.ShouldBeOfType<ContinuableFailedIssue>();
        continuableFailed.FailureCategory.ShouldBe("usage_limited");
    }
}
