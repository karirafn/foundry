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
        Seed(new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(7)
            .TriageBlockedFromTriage(verdict));
        WorkerRunId runId = WorkerRunId.New();

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
    public async Task WhenIssueIsInAnotherState_LeavesItUnchangedAndLogsInformation()
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
        _logger.Entries.ShouldSatisfyAllConditions(
            () => _logger.Entries.ShouldContain(entry => entry.Level == LogLevel.Information),
            () => _dbContext.Set<Issue>().AsNoTracking().ToList().ShouldHaveSingleItem().ShouldBeOfType<TriageFailedIssue>().FailureCategory.ShouldNotBe(FailureCategory.BlockerLinkFailed));
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
}
