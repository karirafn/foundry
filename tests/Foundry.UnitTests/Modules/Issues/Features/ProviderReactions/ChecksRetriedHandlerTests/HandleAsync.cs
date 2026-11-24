using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Issues.Features.ProviderReactions;
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

namespace Foundry.UnitTests.Modules.Issues.Features.ProviderReactions.ChecksRetriedHandlerTests;

public sealed class HandleAsync : IAsyncDisposable
{
    private const string RedHead = "4f2c9e1a7b3d5f6e8a0c2b4d6f8e0a1c3b5d7f9e";

    private static readonly DateTimeOffset Now = new(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly CapturingDomainEventDispatcher _dispatcher;
    private readonly IIntegrationEventHandler<ChecksRetried> _sut;

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
        _sut = new ChecksRetriedHandler(_dbContext, _dispatcher, NullLogger<ChecksRetriedHandler>.Instance);
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private void Seed(Issue issue)
    {
        _dbContext.Set<Issue>().Add(issue);
        _dbContext.SaveChanges();
        _dbContext.ChangeTracker.Clear();
    }

    private async Task<Issue?> LoadAsync(MonitoredRepositoryId repositoryId)
    {
        _dbContext.ChangeTracker.Clear();
        return await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(i => i.MonitoredRepositoryId == repositoryId, TestContext.Current.CancellationToken);
    }

    private static ChecksPendingIssue OwingRetry(MonitoredRepositoryId repositoryId) =>
        new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(1)
            .ChecksPending()
            .FixChecks([new FailingCheck("build", "failure", null)], Now, RedHead)
            .Claim(WorkerRunId.New(), Now)
            .MarkUnchangedRebasePending(Now, Now)
            .MarkChecksPending(new RebaseOutcome.UpToDate(), Now);

    [Fact]
    public async Task WhenARetryIsOwedForTheHead_StaysInTheGateWithTheRetrySpent()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        Seed(OwingRetry(repositoryId));
        DateTimeOffset retriedAt = Now.AddMinutes(20);

        // Act
        await _sut.HandleAsync(new ChecksRetried(repositoryId, 1, RedHead, retriedAt), CancellationToken.None);

        // Assert
        ChecksPendingIssue checksPending = (await LoadAsync(repositoryId)).ShouldBeOfType<ChecksPendingIssue>();
        checksPending.ShouldSatisfyAllConditions(
            () => checksPending.OwedChecksRetryHeadSha.ShouldBeNull(),
            () => checksPending.ChecksRetriedAt.ShouldBe(retriedAt),
            () => checksPending.EnteredChecksPendingAt.ShouldBe(retriedAt),
            () => _dispatcher.DispatchedEvents.OfType<IssueChecksPending>().ShouldHaveSingleItem());
    }

    [Fact]
    public async Task WhenNoRetryIsOwedForTheHead_ChangesNothing()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        Seed(OwingRetry(repositoryId));

        // Act
        await _sut.HandleAsync(
            new ChecksRetried(repositoryId, 1, "9a8b7c6d5e4f3a2b1c0d9e8f7a6b5c4d3e2f1a0b", Now),
            CancellationToken.None);

        // Assert
        ChecksPendingIssue checksPending = (await LoadAsync(repositoryId)).ShouldBeOfType<ChecksPendingIssue>();
        checksPending.ShouldSatisfyAllConditions(
            () => checksPending.OwedChecksRetryHeadSha.ShouldBe(RedHead),
            () => checksPending.ChecksRetriedAt.ShouldBeNull());
    }
}
