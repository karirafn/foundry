using Foundry.WebApi.Modules.Issues;
using Foundry.WebApi.Modules.Issues.Domain;
using Foundry.WebApi.Modules.Issues.Features;
using Foundry.WebApi.Modules.Monitoring.Domain;
using Foundry.WebApi.Shared.Abstractions;
using Foundry.WebApi.Shared.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.WebApi.UnitTests.Modules.Issues.Features.ProcessIssueDependenciesHandlerTests;

public sealed class HandleAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly CapturingDomainEventDispatcher _dispatcher;
    private readonly IDomainEventHandler<IssueDependenciesDetected> _sut;

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
        _dispatcher = new CapturingDomainEventDispatcher();
        _sut = new ProcessIssueDependenciesHandler(
            _dbContext,
            new IssuesModule(_dbContext),
            _dispatcher,
            NullLogger<ProcessIssueDependenciesHandler>.Instance);
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private DetectedIssue SeedDetectedIssue(MonitoredRepositoryId repositoryId, int issueNumber)
    {
        DetectedIssue issue = DetectedIssue.Detect(
            repositoryId,
            issueNumber,
            title: $"Issue {issueNumber}",
            body: "Body",
            author: ValidAuthor,
            url: ValidUrl,
            labels: [],
            detectedAt: DateTimeOffset.UtcNow);
        _dbContext.Set<Issue>().Add(issue);
        _dbContext.SaveChanges();
        _dbContext.ChangeTracker.Clear();
        return issue;
    }

    private QueuedIssue SeedQueuedIssue(MonitoredRepositoryId repositoryId, int issueNumber)
    {
        DetectedIssue detected = DetectedIssue.Detect(
            repositoryId,
            issueNumber,
            title: $"Issue {issueNumber}",
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

    private BlockedIssue SeedBlockedIssue(
        MonitoredRepositoryId repositoryId,
        int issueNumber,
        IReadOnlyList<int> blockedBy)
    {
        DetectedIssue detected = DetectedIssue.Detect(
            repositoryId,
            issueNumber,
            title: $"Issue {issueNumber}",
            body: "Body",
            author: ValidAuthor,
            url: ValidUrl,
            labels: [],
            detectedAt: DateTimeOffset.UtcNow);
        BlockedIssue blocked = detected.Block(blockedBy);
        _dbContext.Set<Issue>().Add(blocked);
        _dbContext.SaveChanges();
        _dbContext.ChangeTracker.Clear();
        return blocked;
    }

    // Behavior (b): handler updates BlockedBy on the issue
    [Fact]
    public async Task WhenIssueExists_UpdatesBlockedBy()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedDetectedIssue(repositoryId, issueNumber: 1);

        IssueDependenciesDetected @event = new(
            MonitoredRepositoryId: repositoryId,
            IssueNumber: 1,
            BlockedByIssueNumbers: [5, 10]);

        // Act
        await _sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dbContext.ChangeTracker.Clear();
        Issue? issue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == repositoryId && i.IssueNumber == 1,
                TestContext.Current.CancellationToken);
        issue.ShouldNotBeNull().BlockedBy.ShouldBe([5, 10]);
    }

    [Fact]
    public async Task WhenIssueDoesNotExist_DoesNotThrow()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();

        IssueDependenciesDetected @event = new(
            MonitoredRepositoryId: repositoryId,
            IssueNumber: 999,
            BlockedByIssueNumbers: []);

        // Act
        Task act = _sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        await Should.NotThrowAsync(act);
    }

    // Behavior (c): DetectedIssue + no blockers → QueuedIssue
    [Fact]
    public async Task WhenDetectedIssueHasNoBlockers_TransitionsToQueuedIssue()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedDetectedIssue(repositoryId, issueNumber: 2);

        IssueDependenciesDetected @event = new(
            MonitoredRepositoryId: repositoryId,
            IssueNumber: 2,
            BlockedByIssueNumbers: []);

        // Act
        await _sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dbContext.ChangeTracker.Clear();
        Issue? issue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == repositoryId && i.IssueNumber == 2,
                TestContext.Current.CancellationToken);
        issue.ShouldBeOfType<QueuedIssue>();
    }

    [Fact]
    public async Task WhenDetectedIssueHasNoBlockers_RaisesIssueQueuedEvent()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedDetectedIssue(repositoryId, issueNumber: 3);

        IssueDependenciesDetected @event = new(
            MonitoredRepositoryId: repositoryId,
            IssueNumber: 3,
            BlockedByIssueNumbers: []);

        // Act
        await _sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dispatcher.DispatchedEvents
            .OfType<IssueQueued>()
            .ShouldHaveSingleItem();
    }

    // Behavior (d): DetectedIssue + has blockers → BlockedIssue
    [Fact]
    public async Task WhenDetectedIssueHasBlockers_TransitionsToBlockedIssue()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedDetectedIssue(repositoryId, issueNumber: 4);

        IssueDependenciesDetected @event = new(
            MonitoredRepositoryId: repositoryId,
            IssueNumber: 4,
            BlockedByIssueNumbers: [7]);

        // Act
        await _sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dbContext.ChangeTracker.Clear();
        Issue? issue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == repositoryId && i.IssueNumber == 4,
                TestContext.Current.CancellationToken);
        issue.ShouldBeOfType<BlockedIssue>();
    }

    [Fact]
    public async Task WhenDetectedIssueHasBlockers_RaisesIssueBlockedEvent()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedDetectedIssue(repositoryId, issueNumber: 5);

        IssueDependenciesDetected @event = new(
            MonitoredRepositoryId: repositoryId,
            IssueNumber: 5,
            BlockedByIssueNumbers: [9]);

        // Act
        await _sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dispatcher.DispatchedEvents
            .OfType<IssueBlocked>()
            .ShouldHaveSingleItem();
    }

    // Behavior (e): QueuedIssue + has blockers → BlockedIssue
    [Fact]
    public async Task WhenQueuedIssueHasBlockers_TransitionsToBlockedIssue()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedQueuedIssue(repositoryId, issueNumber: 6);

        IssueDependenciesDetected @event = new(
            MonitoredRepositoryId: repositoryId,
            IssueNumber: 6,
            BlockedByIssueNumbers: [11]);

        // Act
        await _sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dbContext.ChangeTracker.Clear();
        Issue? issue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == repositoryId && i.IssueNumber == 6,
                TestContext.Current.CancellationToken);
        issue.ShouldBeOfType<BlockedIssue>();
    }

    [Fact]
    public async Task WhenQueuedIssueHasBlockers_RaisesIssueBlockedEvent()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedQueuedIssue(repositoryId, issueNumber: 7);

        IssueDependenciesDetected @event = new(
            MonitoredRepositoryId: repositoryId,
            IssueNumber: 7,
            BlockedByIssueNumbers: [13]);

        // Act
        await _sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dispatcher.DispatchedEvents
            .OfType<IssueBlocked>()
            .ShouldHaveSingleItem();
    }

    // Behavior (f): BlockedIssue + cleared blockers → QueuedIssue
    [Fact]
    public async Task WhenBlockedIssueHasNoMoreBlockers_TransitionsToQueuedIssue()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedBlockedIssue(repositoryId, issueNumber: 8, blockedBy: [15]);

        IssueDependenciesDetected @event = new(
            MonitoredRepositoryId: repositoryId,
            IssueNumber: 8,
            BlockedByIssueNumbers: []);

        // Act
        await _sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dbContext.ChangeTracker.Clear();
        Issue? issue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == repositoryId && i.IssueNumber == 8,
                TestContext.Current.CancellationToken);
        issue.ShouldBeOfType<QueuedIssue>();
    }

    [Fact]
    public async Task WhenBlockedIssueHasNoMoreBlockers_RaisesIssueQueuedEvent()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedBlockedIssue(repositoryId, issueNumber: 9, blockedBy: [17]);

        IssueDependenciesDetected @event = new(
            MonitoredRepositoryId: repositoryId,
            IssueNumber: 9,
            BlockedByIssueNumbers: []);

        // Act
        await _sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dispatcher.DispatchedEvents
            .OfType<IssueQueued>()
            .ShouldHaveSingleItem();
    }

    // No-transition cases
    [Fact]
    public async Task WhenBlockedIssueStillHasBlockers_NoTransition()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedBlockedIssue(repositoryId, issueNumber: 10, blockedBy: [20]);

        IssueDependenciesDetected @event = new(
            MonitoredRepositoryId: repositoryId,
            IssueNumber: 10,
            BlockedByIssueNumbers: [21]);

        // Act
        await _sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dbContext.ChangeTracker.Clear();
        Issue? issue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == repositoryId && i.IssueNumber == 10,
                TestContext.Current.CancellationToken);
        issue.ShouldBeOfType<BlockedIssue>();
        _dispatcher.DispatchedEvents.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenQueuedIssueHasNoBlockers_NoTransition()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedQueuedIssue(repositoryId, issueNumber: 11);

        IssueDependenciesDetected @event = new(
            MonitoredRepositoryId: repositoryId,
            IssueNumber: 11,
            BlockedByIssueNumbers: []);

        // Act
        await _sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dbContext.ChangeTracker.Clear();
        Issue? issue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == repositoryId && i.IssueNumber == 11,
                TestContext.Current.CancellationToken);
        issue.ShouldBeOfType<QueuedIssue>();
        _dispatcher.DispatchedEvents.ShouldBeEmpty();
    }

    // Behavior (g): circular dependency detected
    [Fact]
    public async Task WhenCycleDetectedIncludingCurrentIssue_RaisesCircularDependencyDetectedEvent()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();

        // Seed issue 1 blocked by issue 2
        SeedBlockedIssue(repositoryId, issueNumber: 1, blockedBy: [2]);
        // Issue 2 is now being processed with dependency on issue 1 → cycle: 1→2→1
        SeedDetectedIssue(repositoryId, issueNumber: 2);

        IssueDependenciesDetected @event = new(
            MonitoredRepositoryId: repositoryId,
            IssueNumber: 2,
            BlockedByIssueNumbers: [1]);

        // Act
        await _sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        CircularDependencyDetected cycleEvent = _dispatcher.DispatchedEvents
            .OfType<CircularDependencyDetected>()
            .ShouldHaveSingleItem();
        cycleEvent.MonitoredRepositoryId.ShouldBe(repositoryId);
        cycleEvent.CyclePath.ShouldContain(2);
    }

    [Fact]
    public async Task WhenCycleDetectedIncludingCurrentIssue_IssueRemainsInCurrentState()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedBlockedIssue(repositoryId, issueNumber: 1, blockedBy: [2]);
        SeedDetectedIssue(repositoryId, issueNumber: 2);

        IssueDependenciesDetected @event = new(
            MonitoredRepositoryId: repositoryId,
            IssueNumber: 2,
            BlockedByIssueNumbers: [1]);

        // Act
        await _sut.HandleAsync(@event, CancellationToken.None);

        // Assert — issue 2 was DetectedIssue, should remain DetectedIssue (no transition on cycle)
        _dbContext.ChangeTracker.Clear();
        Issue? issue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == repositoryId && i.IssueNumber == 2,
                TestContext.Current.CancellationToken);

        Issue reloaded = issue.ShouldNotBeNull();
        reloaded.ShouldBeOfType<DetectedIssue>();
        // BlockedBy should still be updated though
        reloaded.BlockedBy.ShouldBe([1]);
    }

    private sealed class CapturingDomainEventDispatcher : IDomainEventDispatcher
    {
        private readonly List<IDomainEvent> _events = [];

        public IReadOnlyList<IDomainEvent> DispatchedEvents => _events;

        public Task DispatchAsync(IEnumerable<IDomainEvent> events, CancellationToken cancellationToken)
        {
            _events.AddRange(events);
            return Task.CompletedTask;
        }
    }
}
