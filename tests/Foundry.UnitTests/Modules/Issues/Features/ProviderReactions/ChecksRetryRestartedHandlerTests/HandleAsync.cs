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

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Features.ProviderReactions.ChecksRetryRestartedHandlerTests;

public sealed class HandleAsync : IAsyncDisposable
{
    private const string RedHead = "4f2c9e1a7b3d5f6e8a0c2b4d6f8e0a1c3b5d7f9e";

    private static readonly DateTimeOffset Now = new(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly IIntegrationEventHandler<ChecksRetryRestarted> _sut;

    public HandleAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new FoundryDbContext(options);
        _dbContext.Database.EnsureCreated();
        _sut = new ChecksRetryRestartedHandler(_dbContext);
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task WhenRetriedJobsRestart_ClearsTheWaitForThem()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        ChecksPendingIssue retried = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(1)
            .ChecksPending()
            .FixChecks([new FailingCheck("build", "failure", null)], Now, RedHead)
            .Claim(WorkerRunId.New(), Now)
            .MarkUnchangedRebasePending(Now, Now)
            .MarkChecksPending(new RebaseOutcome.UpToDate(), Now)
            .RecordChecksRetried(RedHead, Now);
        _dbContext.Set<Issue>().Add(retried);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        await _sut.HandleAsync(new ChecksRetryRestarted(repositoryId, 1), CancellationToken.None);

        // Assert
        _dbContext.ChangeTracker.Clear();
        Issue reloaded = await _dbContext.Set<Issue>()
            .FirstAsync(i => i.MonitoredRepositoryId == repositoryId, TestContext.Current.CancellationToken);
        reloaded.ShouldBeOfType<ChecksPendingIssue>().ChecksRetriedAt.ShouldBeNull();
    }
}
