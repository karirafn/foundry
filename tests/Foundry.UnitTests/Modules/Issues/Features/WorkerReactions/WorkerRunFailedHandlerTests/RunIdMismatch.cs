using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
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

public sealed class RunIdMismatch : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly CapturingDomainEventDispatcher _dispatcher;
    private readonly IIntegrationEventHandler<WorkerRunFailed> _sut;

    public RunIdMismatch()
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

    [Fact]
    public async Task WhenRunIdDiffersFromIssueRunId_IssueRemainsInProgress()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        InProgressIssue inProgress = SeedInProgressIssue(repositoryId);
        Guid staleRunId = Guid.NewGuid();

        WorkerRunFailed @event = new(
            WorkerRunId: staleRunId,
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
        InProgressIssue stillInProgress = issue.ShouldBeOfType<InProgressIssue>();
        stillInProgress.WorkerRunId.ShouldBe(inProgress.WorkerRunId);
    }
}
