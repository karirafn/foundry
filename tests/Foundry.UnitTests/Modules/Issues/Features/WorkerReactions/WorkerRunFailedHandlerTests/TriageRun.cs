using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Features.WorkerReactions;
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

public sealed class TriageRun : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly IIntegrationEventHandler<WorkerRunFailed> _sut;

    public TriageRun()
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
            new CapturingDomainEventDispatcher(),
            NullLogger<WorkerRunFailedHandler>.Instance);
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private TriageInProgressIssue SeedTriageInProgress(WorkerRunId workerRunId)
    {
        TriageInProgressIssue triaging = new IssueBuilder().WithWorkerRunId(workerRunId).TriageInProgress();
        _dbContext.Set<Issue>().Add(triaging);
        _dbContext.SaveChanges();
        _dbContext.ChangeTracker.Clear();
        return triaging;
    }

    [Fact]
    public async Task WhenTheTriageRunFails_TheIssueGoesToTriageFailedWithTheReason()
    {
        // Arrange
        WorkerRunId workerRunId = WorkerRunId.New();
        TriageInProgressIssue triaging = SeedTriageInProgress(workerRunId);
        WorkerRunFailed @event = new(
            workerRunId,
            triaging.Id.Value,
            "Verdict missing from output",
            FailureCategory.VerdictInvalidToken);

        // Act
        await _sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dbContext.ChangeTracker.Clear();
        Issue? reloaded = await _dbContext.Set<Issue>().FindAsync([triaging.Id], TestContext.Current.CancellationToken);
        TriageFailedIssue failed = reloaded.ShouldBeOfType<TriageFailedIssue>();
        failed.ShouldSatisfyAllConditions(
            () => failed.WorkerRunId.ShouldBe(workerRunId),
            () => failed.FailureReason.ShouldBe("Verdict missing from output"),
            () => failed.FailureCategory.ShouldBe(FailureCategory.VerdictInvalid));
    }

    [Fact]
    public async Task WhenTheRunIdIsStale_TheIssueStaysInTriage()
    {
        // Arrange
        TriageInProgressIssue triaging = SeedTriageInProgress(WorkerRunId.New());
        WorkerRunFailed @event = new(WorkerRunId.New(), triaging.Id.Value, "Worker run cancelled", FailureCategory.CancelledToken);

        // Act
        await _sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dbContext.ChangeTracker.Clear();
        Issue? reloaded = await _dbContext.Set<Issue>().FindAsync([triaging.Id], TestContext.Current.CancellationToken);
        reloaded.ShouldBeOfType<TriageInProgressIssue>();
    }
}
