using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Issues.Features.Claiming;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Features.Claiming.ClaimTimeRecheckTests;

public sealed class CheckAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly CapturingDomainEventDispatcher _domainEventDispatcher = new();

    public CheckAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new FoundryDbContext(options);
        _dbContext.Database.EnsureCreated();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task WhenFreshCandidateHasNoOpenBlockers_ReturnsClearAfterReadingBlockers()
    {
        // Arrange
        FreshQueuedIssue queued = Seed(new IssueBuilder().WithIssueNumber(7).FreshQueued());
        StubIssueBlockerQueries blockerQueries = new();
        ClaimTimeRecheck sut = ClaimTimeRecheckFactory.Create(_dbContext, blockerQueries, _domainEventDispatcher);

        // Act
        ClaimTimeRecheckOutcome outcome = await sut.CheckAsync(queued, CancellationToken.None);

        // Assert
        outcome.ShouldBe(ClaimTimeRecheckOutcome.Clear);
        blockerQueries.RequestedIssueNumbers.ShouldBe([7]);
    }

    [Fact]
    public async Task WhenFreshCandidateGainedBlocker_ReturnsBlockedAndTransitionsToBlockedIssue()
    {
        // Arrange
        FreshQueuedIssue queued = Seed(new IssueBuilder().WithIssueNumber(1).FreshQueued());
        StubIssueBlockerQueries blockerQueries = new StubIssueBlockerQueries().WithBlockers(1, 99);
        ClaimTimeRecheck sut = ClaimTimeRecheckFactory.Create(_dbContext, blockerQueries, _domainEventDispatcher);

        // Act
        ClaimTimeRecheckOutcome outcome = await sut.CheckAsync(queued, CancellationToken.None);

        // Assert
        outcome.ShouldBe(ClaimTimeRecheckOutcome.Blocked);
        (await ReloadAsync(queued)).ShouldBeOfType<BlockedIssue>().BlockedBy.ShouldBe([99]);
        _domainEventDispatcher.DispatchedEvents.OfType<IssueBlocked>().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task WhenTriageCandidateGainedBlocker_ReturnsBlockedAndTransitionsToTriageBlockedIssue()
    {
        // Arrange
        TriageQueuedIssue queued = Seed(new IssueBuilder().WithIssueNumber(1).TriageQueued());
        StubIssueBlockerQueries blockerQueries = new StubIssueBlockerQueries().WithBlockers(1, 99);
        ClaimTimeRecheck sut = ClaimTimeRecheckFactory.Create(_dbContext, blockerQueries, _domainEventDispatcher);

        // Act
        ClaimTimeRecheckOutcome outcome = await sut.CheckAsync(queued, CancellationToken.None);

        // Assert
        outcome.ShouldBe(ClaimTimeRecheckOutcome.Blocked);
        (await ReloadAsync(queued)).ShouldBeOfType<TriageBlockedIssue>().BlockedBy.ShouldBe([99]);
        _domainEventDispatcher.DispatchedEvents.OfType<IssueTriageBlocked>().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task WhenBlockerReadFails_ReturnsReadFailedWithoutStateChange()
    {
        // Arrange
        FreshQueuedIssue queued = Seed(new IssueBuilder().WithIssueNumber(1).FreshQueued());
        StubIssueBlockerQueries blockerQueries = new StubIssueBlockerQueries().WithFailure(1);
        ClaimTimeRecheck sut = ClaimTimeRecheckFactory.Create(_dbContext, blockerQueries, _domainEventDispatcher);

        // Act
        ClaimTimeRecheckOutcome outcome = await sut.CheckAsync(queued, CancellationToken.None);

        // Assert
        outcome.ShouldBe(ClaimTimeRecheckOutcome.ReadFailed);
        (await ReloadAsync(queued)).ShouldBeOfType<FreshQueuedIssue>();
        _domainEventDispatcher.DispatchedEvents.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenClaimTimeBlockerFormsCycle_ReturnsBlockedAndRaisesCircularDependency()
    {
        // Arrange — issue 1 is already blocked by 2; issue 2 now reports 1 as a blocker
        MonitoredRepositoryId repoId = MonitoredRepositoryId.New();
        Seed(new IssueBuilder().WithMonitoredRepositoryId(repoId).WithIssueNumber(1).Detected().Block([2]));
        FreshQueuedIssue queued = Seed(
            new IssueBuilder().WithMonitoredRepositoryId(repoId).WithIssueNumber(2).FreshQueued());
        StubIssueBlockerQueries blockerQueries = new StubIssueBlockerQueries().WithBlockers(2, 1);
        ClaimTimeRecheck sut = ClaimTimeRecheckFactory.Create(_dbContext, blockerQueries, _domainEventDispatcher);

        // Act
        ClaimTimeRecheckOutcome outcome = await sut.CheckAsync(queued, CancellationToken.None);

        // Assert
        outcome.ShouldBe(ClaimTimeRecheckOutcome.Blocked);
        (await ReloadAsync(queued)).ShouldBeOfType<BlockedIssue>().BlockedBy.ShouldBe([1]);
        _domainEventDispatcher.DispatchedEvents.OfType<CircularDependencyDetected>().ShouldHaveSingleItem();
    }

    private TIssue Seed<TIssue>(TIssue issue)
        where TIssue : Issue
    {
        _dbContext.Set<Issue>().Add(issue);
        _dbContext.SaveChanges();
        _dbContext.ChangeTracker.Clear();
        return issue;
    }

    private async Task<Issue> ReloadAsync(Issue issue)
    {
        _dbContext.ChangeTracker.Clear();
        Issue? reloaded = await _dbContext.Set<Issue>().FindAsync([issue.Id], TestContext.Current.CancellationToken);
        return reloaded.ShouldNotBeNull();
    }
}
