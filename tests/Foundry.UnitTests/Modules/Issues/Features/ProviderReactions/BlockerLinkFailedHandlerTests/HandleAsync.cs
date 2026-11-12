using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Issues.Features.ProviderReactions;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Features.ProviderReactions.BlockerLinkFailedHandlerTests;

public sealed class HandleAsync : IAsyncDisposable
{
    private const int BlockerNumber = 15;
    private const string ProviderMessage = "issue is locked";

    private static readonly DateTimeOffset DecidedAt = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset FailedAt = new(2026, 11, 11, 12, 5, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly CapturingDomainEventDispatcher _dispatcher;
    private readonly CapturingLogger<BlockerLinkFailedHandler> _logger;
    private readonly IIntegrationEventHandler<BlockerLinkFailed> _sut;

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
        _logger = new CapturingLogger<BlockerLinkFailedHandler>();
        _sut = new BlockerLinkFailedHandler(_dbContext, _dispatcher, _logger);
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private static BlockerLinkFailed EventFor(MonitoredRepositoryId repositoryId, int issueNumber, Guid runId) =>
        new(repositoryId, issueNumber, runId, BlockerNumber, ProviderMessage, FailedAt);

    private void Seed(Issue issue)
    {
        _dbContext.Set<Issue>().Add(issue);
        _dbContext.SaveChanges();
        _dbContext.ChangeTracker.Clear();
    }

    [Fact]
    public async Task WhenIssueIsTriageBlocked_FailsTriageWithTheRunAndTheTimeFromTheEvent()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        TriageVerdict.Blocked verdict = new(
            IssueKind.Feature,
            [new TriageBlocker(BlockerNumber, "Needs it first.")],
            DecidedAt);
        WorkerRunId runId = WorkerRunId.New();
        Seed(new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(7)
            .WithWorkerRunId(runId)
            .TriageBlockedFromTriage(verdict));

        // Act
        await _sut.HandleAsync(EventFor(repositoryId, 7, runId.Value), CancellationToken.None);

        // Assert
        _dbContext.ChangeTracker.Clear();
        List<Issue> issues = await _dbContext.Set<Issue>().ToListAsync(TestContext.Current.CancellationToken);
        TriageFailedIssue failed = issues.ShouldHaveSingleItem().ShouldBeOfType<TriageFailedIssue>();
        failed.ShouldSatisfyAllConditions(
            () => failed.FailureCategory.ShouldBe(FailureCategory.BlockerLinkFailed),
            () => failed.WorkerRunId.ShouldBe(runId),
            () => failed.FailedAt.ShouldBe(FailedAt));
    }

    [Fact]
    public async Task WhenEventIsFromAnotherTriageRun_LeavesTheIssueTriageBlocked()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        TriageVerdict.Blocked verdict = new(
            IssueKind.Feature,
            [new TriageBlocker(BlockerNumber, "Needs it first.")],
            DecidedAt);
        Seed(new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(7)
            .TriageBlockedFromTriage(verdict));

        // Act
        await _sut.HandleAsync(EventFor(repositoryId, 7, Guid.NewGuid()), CancellationToken.None);

        // Assert
        await AssertSingleIssueIsStillTriageBlockedAsync();
    }

    [Fact]
    public async Task WhenBlockerIsNotNamedByTheVerdict_LeavesTheIssueTriageBlocked()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        TriageVerdict.Blocked verdict = new(
            IssueKind.Feature,
            [new TriageBlocker(BlockerNumber + 1, "Needs it first.")],
            DecidedAt);
        WorkerRunId runId = WorkerRunId.New();
        Seed(new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(7)
            .WithWorkerRunId(runId)
            .TriageBlockedFromTriage(verdict));

        // Act
        await _sut.HandleAsync(EventFor(repositoryId, 7, runId.Value), CancellationToken.None);

        // Assert
        await AssertSingleIssueIsStillTriageBlockedAsync();
    }

    [Fact]
    public async Task WhenIssueWaitsOnBlockersFoundBeforeTriage_LeavesTheIssueTriageBlocked()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        Seed(new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(7)
            .TriageBlocked([BlockerNumber]));

        // Act
        await _sut.HandleAsync(EventFor(repositoryId, 7, Guid.NewGuid()), CancellationToken.None);

        // Assert
        await AssertSingleIssueIsStillTriageBlockedAsync();
    }

    [Fact]
    public async Task WhenEventIsFromAnotherTriageRun_LogsThatItIsFromAnotherRun()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        TriageVerdict.Blocked verdict = new(
            IssueKind.Feature,
            [new TriageBlocker(BlockerNumber, "Needs it first.")],
            DecidedAt);
        Seed(new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(7)
            .TriageBlockedFromTriage(verdict));

        // Act
        await _sut.HandleAsync(EventFor(repositoryId, 7, Guid.NewGuid()), CancellationToken.None);

        // Assert
        _logger.Entries.ShouldContain(entry => entry.Message.Contains("another run"));
    }

    [Fact]
    public async Task WhenBlockerIsNotNamedByTheVerdict_LogsThatTheVerdictDoesNotNameIt()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        TriageVerdict.Blocked verdict = new(
            IssueKind.Feature,
            [new TriageBlocker(BlockerNumber + 1, "Needs it first.")],
            DecidedAt);
        WorkerRunId runId = WorkerRunId.New();
        Seed(new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(7)
            .WithWorkerRunId(runId)
            .TriageBlockedFromTriage(verdict));

        // Act
        await _sut.HandleAsync(EventFor(repositoryId, 7, runId.Value), CancellationToken.None);

        // Assert
        _logger.Entries.ShouldContain(entry => entry.Message.Contains("not named by the verdict"));
    }

    [Fact]
    public async Task WhenIssueIsInAnotherState_KeepsItsFailureCategory()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        Seed(new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(7)
            .WithFailureCategory(FailureCategory.NonZeroExit)
            .TriageFailed());

        // Act
        await _sut.HandleAsync(EventFor(repositoryId, 7, Guid.NewGuid()), CancellationToken.None);

        // Assert
        List<Issue> issues = await _dbContext.Set<Issue>()
            .AsNoTracking()
            .ToListAsync(TestContext.Current.CancellationToken);
        issues.ShouldHaveSingleItem().ShouldBeOfType<TriageFailedIssue>().FailureCategory.ShouldBe(FailureCategory.NonZeroExit);
    }

    [Fact]
    public async Task WhenIssueIsInAnotherState_LogsInformation()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        Seed(new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(7)
            .TriageFailed());

        // Act
        await _sut.HandleAsync(EventFor(repositoryId, 7, Guid.NewGuid()), CancellationToken.None);

        // Assert
        _logger.Entries.ShouldContain(entry => entry.Level == LogLevel.Information);
    }

    [Fact]
    public async Task WhenIssueIsNotFound_LogsInformation()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();

        // Act
        await _sut.HandleAsync(EventFor(repositoryId, 7, Guid.NewGuid()), CancellationToken.None);

        // Assert
        _logger.Entries.ShouldContain(entry => entry.Level == LogLevel.Information);
    }

    private async Task AssertSingleIssueIsStillTriageBlockedAsync()
    {
        List<Issue> issues = await _dbContext.Set<Issue>()
            .AsNoTracking()
            .ToListAsync(TestContext.Current.CancellationToken);
        issues.ShouldHaveSingleItem().ShouldBeOfType<TriageBlockedIssue>();
    }
}
