using Foundry.Modules.Issues.Domain;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Issues.Features;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Events;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Features.IssueEligibilityCheckedHandlerTests;

public sealed class HandleAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly CapturingDomainEventDispatcher _dispatcher;
    private readonly IIntegrationEventHandler<IssueEligibilityChecked> _sut;

    private static IssueAuthor ValidAuthor =>
        ((Result<IssueAuthor>.Success)IssueAuthor.Create("octocat")).Value;

    private static ProviderUrl ValidUrl =>
        ((Result<ProviderUrl>.Success)ProviderUrl.Create("https://github.com/owner/repo/issues/1")).Value;

    private static readonly EligibilityViolationInfo ViolationInfo =
        new("branch-protection:allow-direct-pushes", "Direct pushes allowed.");

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
        _sut = new IssueEligibilityCheckedHandler(
            _dbContext,
            _dispatcher,
            NullLogger<IssueEligibilityCheckedHandler>.Instance);
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

    private IneligibleIssue SeedIneligibleIssue(
        MonitoredRepositoryId repositoryId,
        int issueNumber,
        IReadOnlyList<int>? blockedBy = null)
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
        IneligibleIssue ineligible = detected.MarkIneligible([EligibilityViolation.AllowDirectPushes()]);
        if (blockedBy is { Count: > 0 })
        {
            ineligible.SetBlockedBy(blockedBy);
        }

        _dbContext.Set<Issue>().Add(ineligible);
        _dbContext.SaveChanges();
        _dbContext.ChangeTracker.Clear();
        return ineligible;
    }

    // Behavior (f): issue not found — logs warning and returns without throwing
    [Fact]
    public async Task WhenIssueDoesNotExist_DoesNotThrow()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();

        IssueEligibilityChecked @event = new(
            MonitoredRepositoryId: repositoryId,
            IssueNumber: 999,
            Violations: [ViolationInfo]);

        // Act
        Task act = _sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        await Should.NotThrowAsync(act);
    }

    // Behavior (a): violations non-empty + DetectedIssue → IneligibleIssue
    [Fact]
    public async Task WhenDetectedIssueHasViolations_TransitionsToIneligibleIssue()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedDetectedIssue(repositoryId, issueNumber: 1);

        IssueEligibilityChecked @event = new(
            MonitoredRepositoryId: repositoryId,
            IssueNumber: 1,
            Violations: [ViolationInfo]);

        // Act
        await _sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dbContext.ChangeTracker.Clear();
        Issue? issue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == repositoryId && i.IssueNumber == 1,
                TestContext.Current.CancellationToken);
        issue.ShouldBeOfType<IneligibleIssue>();
    }

    [Fact]
    public async Task WhenDetectedIssueHasViolations_PersistsViolations()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedDetectedIssue(repositoryId, issueNumber: 2);

        IssueEligibilityChecked @event = new(
            MonitoredRepositoryId: repositoryId,
            IssueNumber: 2,
            Violations: [ViolationInfo]);

        // Act
        await _sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dbContext.ChangeTracker.Clear();
        IneligibleIssue ineligible = _dbContext.Set<Issue>()
            .OfType<IneligibleIssue>()
            .ShouldHaveSingleItem();
        ineligible.Violations.ShouldHaveSingleItem()
            .ShouldSatisfyAllConditions(
                v => v.Rule.ShouldBe(ViolationInfo.Rule),
                v => v.Description.ShouldBe(ViolationInfo.Description));
    }

    [Fact]
    public async Task WhenDetectedIssueHasViolations_RaisesIssueIneligibleEvent()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedDetectedIssue(repositoryId, issueNumber: 3);

        IssueEligibilityChecked @event = new(
            MonitoredRepositoryId: repositoryId,
            IssueNumber: 3,
            Violations: [ViolationInfo]);

        // Act
        await _sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dispatcher.DispatchedEvents
            .OfType<IssueIneligible>()
            .ShouldHaveSingleItem();
    }

    // Behavior (b): violations non-empty + IneligibleIssue → updates violations
    [Fact]
    public async Task WhenIneligibleIssueHasViolations_UpdatesViolations()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedIneligibleIssue(repositoryId, issueNumber: 4);

        EligibilityViolationInfo newViolation =
            new("branch-protection:allow-force-pushes", "Force pushes allowed.");
        IssueEligibilityChecked @event = new(
            MonitoredRepositoryId: repositoryId,
            IssueNumber: 4,
            Violations: [newViolation]);

        // Act
        await _sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dbContext.ChangeTracker.Clear();
        IneligibleIssue ineligible = _dbContext.Set<Issue>()
            .OfType<IneligibleIssue>()
            .ShouldHaveSingleItem();
        ineligible.Violations.ShouldHaveSingleItem()
            .ShouldSatisfyAllConditions(
                v => v.Rule.ShouldBe(newViolation.Rule),
                v => v.Description.ShouldBe(newViolation.Description));
    }

    [Fact]
    public async Task WhenIneligibleIssueHasViolations_RemainsIneligibleIssue()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedIneligibleIssue(repositoryId, issueNumber: 5);

        IssueEligibilityChecked @event = new(
            MonitoredRepositoryId: repositoryId,
            IssueNumber: 5,
            Violations: [ViolationInfo]);

        // Act
        await _sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dbContext.ChangeTracker.Clear();
        Issue? issue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == repositoryId && i.IssueNumber == 5,
                TestContext.Current.CancellationToken);
        issue.ShouldBeOfType<IneligibleIssue>();
    }

    // Behavior (c): violations empty + IneligibleIssue with no blockers → QueuedIssue
    [Fact]
    public async Task WhenIneligibleIssueHasNoViolationsAndNoBlockers_TransitionsToQueuedIssue()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedIneligibleIssue(repositoryId, issueNumber: 6, blockedBy: []);

        IssueEligibilityChecked @event = new(
            MonitoredRepositoryId: repositoryId,
            IssueNumber: 6,
            Violations: []);

        // Act
        await _sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dbContext.ChangeTracker.Clear();
        Issue? issue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == repositoryId && i.IssueNumber == 6,
                TestContext.Current.CancellationToken);
        issue.ShouldBeOfType<QueuedIssue>();
    }

    [Fact]
    public async Task WhenIneligibleIssueHasNoViolationsAndNoBlockers_RaisesIssueQueuedEvent()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedIneligibleIssue(repositoryId, issueNumber: 7, blockedBy: []);

        IssueEligibilityChecked @event = new(
            MonitoredRepositoryId: repositoryId,
            IssueNumber: 7,
            Violations: []);

        // Act
        await _sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dispatcher.DispatchedEvents
            .OfType<IssueQueued>()
            .ShouldHaveSingleItem();
    }

    // Behavior (d): violations empty + IneligibleIssue with blockers → BlockedIssue
    [Fact]
    public async Task WhenIneligibleIssueHasNoViolationsButHasBlockers_TransitionsToBlockedIssue()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedIneligibleIssue(repositoryId, issueNumber: 8, blockedBy: [10, 11]);

        IssueEligibilityChecked @event = new(
            MonitoredRepositoryId: repositoryId,
            IssueNumber: 8,
            Violations: []);

        // Act
        await _sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dbContext.ChangeTracker.Clear();
        Issue? issue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == repositoryId && i.IssueNumber == 8,
                TestContext.Current.CancellationToken);
        issue.ShouldBeOfType<BlockedIssue>();
    }

    [Fact]
    public async Task WhenIneligibleIssueHasNoViolationsButHasBlockers_RaisesIssueBlockedEvent()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedIneligibleIssue(repositoryId, issueNumber: 9, blockedBy: [12]);

        IssueEligibilityChecked @event = new(
            MonitoredRepositoryId: repositoryId,
            IssueNumber: 9,
            Violations: []);

        // Act
        await _sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dispatcher.DispatchedEvents
            .OfType<IssueBlocked>()
            .ShouldHaveSingleItem();
    }

    // Behavior (e): violations empty + DetectedIssue → does nothing
    [Fact]
    public async Task WhenDetectedIssueHasNoViolations_RemainsDetectedIssue()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedDetectedIssue(repositoryId, issueNumber: 10);

        IssueEligibilityChecked @event = new(
            MonitoredRepositoryId: repositoryId,
            IssueNumber: 10,
            Violations: []);

        // Act
        await _sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dbContext.ChangeTracker.Clear();
        Issue? issue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == repositoryId && i.IssueNumber == 10,
                TestContext.Current.CancellationToken);
        issue.ShouldBeOfType<DetectedIssue>();
    }

    [Fact]
    public async Task WhenDetectedIssueHasNoViolations_NoDomainEventsDispatched()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedDetectedIssue(repositoryId, issueNumber: 11);

        IssueEligibilityChecked @event = new(
            MonitoredRepositoryId: repositoryId,
            IssueNumber: 11,
            Violations: []);

        // Act
        await _sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dispatcher.DispatchedEvents.ShouldBeEmpty();
    }

}
