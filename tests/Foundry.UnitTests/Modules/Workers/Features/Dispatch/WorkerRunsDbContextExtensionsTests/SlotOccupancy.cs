using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Dispatch;
using Foundry.Shared;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Dispatch.WorkerRunsDbContextExtensionsTests;

public sealed class SlotOccupancy : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;

    public SlotOccupancy()
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

    private async Task<StartingRun> SeedStartingRunAsync()
    {
        IssueId issueId = IssueId.New();
        StartingRun starting = StartingRun.Begin(issueId, WorkerRunId.New());
        _dbContext.Set<WorkerRun>().Add(starting);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();
        return starting;
    }

    private async Task<ActiveRun> SeedActiveRunAsync()
    {
        IssueId issueId = IssueId.New();
        StartingRun starting = StartingRun.Begin(issueId, WorkerRunId.New());
        ActiveRun active = starting.Activate(
            ContainerId.From("container-" + Guid.NewGuid()),
            BranchName.From("feat/1-active"),
            MonitoredRepositoryId.New());
        _dbContext.Set<WorkerRun>().Add(active);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();
        return active;
    }

    private async Task<CompletedRun> SeedCompletedRunAsync()
    {
        IssueId issueId = IssueId.New();
        StartingRun starting = StartingRun.Begin(issueId, WorkerRunId.New());
        ActiveRun active = starting.Activate(
            ContainerId.From("container-" + Guid.NewGuid()),
            BranchName.From("feat/2-completed"),
            MonitoredRepositoryId.New());
        CompletedRun completed = active.Complete(exitCode: 0, branchName: null, pullRequestUrl: null);
        _dbContext.Set<WorkerRun>().Add(completed);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();
        return completed;
    }

    private async Task<FailedRun> SeedFailedRunAsync()
    {
        IssueId issueId = IssueId.New();
        StartingRun starting = StartingRun.Begin(issueId, WorkerRunId.New());
        FailedRun failed = starting.Fail(new FailureReason.ContainerError("error"));
        _dbContext.Set<WorkerRun>().Add(failed);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();
        return failed;
    }

    [Fact]
    public async Task GetSlotOccupancyCountAsync_WhenNoRuns_ReturnsZero()
    {
        // Arrange
        // (database is empty)

        // Act
        int count = await _dbContext.GetSlotOccupancyCountAsync(TestContext.Current.CancellationToken);

        // Assert
        count.ShouldBe(0);
    }

    [Fact]
    public async Task GetSlotOccupancyCountAsync_WhenStartingAndActiveRunsExist_CountsBoth()
    {
        // Arrange
        await SeedStartingRunAsync();
        await SeedActiveRunAsync();
        await SeedCompletedRunAsync();
        await SeedFailedRunAsync();

        // Act
        int count = await _dbContext.GetSlotOccupancyCountAsync(TestContext.Current.CancellationToken);

        // Assert
        count.ShouldBe(2);
    }

    [Fact]
    public async Task GetSlotOccupancyCountAsync_WhenOnlyStartingRuns_CountsStarting()
    {
        // Arrange
        await SeedStartingRunAsync();
        await SeedStartingRunAsync();

        // Act
        int count = await _dbContext.GetSlotOccupancyCountAsync(TestContext.Current.CancellationToken);

        // Assert
        count.ShouldBe(2);
    }

    [Fact]
    public async Task GetSlotOccupancyCountAsync_WhenOnlyTerminalRuns_ReturnsZero()
    {
        // Arrange
        await SeedCompletedRunAsync();
        await SeedFailedRunAsync();

        // Act
        int count = await _dbContext.GetSlotOccupancyCountAsync(TestContext.Current.CancellationToken);

        // Assert
        count.ShouldBe(0);
    }

    [Fact]
    public async Task GetSlotOccupancyRunIdsAsync_WhenNoRuns_ReturnsEmptySet()
    {
        // Arrange
        // (database is empty)

        // Act
        IReadOnlySet<WorkerRunId> ids = await _dbContext.GetSlotOccupancyRunIdsAsync(TestContext.Current.CancellationToken);

        // Assert
        ids.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetSlotOccupancyRunIdsAsync_WhenStartingAndActiveRunsExist_ReturnsTheirIds()
    {
        // Arrange
        StartingRun starting = await SeedStartingRunAsync();
        ActiveRun active = await SeedActiveRunAsync();
        await SeedCompletedRunAsync();
        await SeedFailedRunAsync();

        // Act
        IReadOnlySet<WorkerRunId> ids = await _dbContext.GetSlotOccupancyRunIdsAsync(TestContext.Current.CancellationToken);

        // Assert
        ids.ShouldSatisfyAllConditions(
            () => ids.Count.ShouldBe(2),
            () => ids.ShouldContain(starting.Id),
            () => ids.ShouldContain(active.Id));
    }

    [Fact]
    public async Task GetSlotOccupancyRunIdsAsync_WhenOnlyTerminalRuns_ReturnsEmptySet()
    {
        // Arrange
        await SeedCompletedRunAsync();
        await SeedFailedRunAsync();

        // Act
        IReadOnlySet<WorkerRunId> ids = await _dbContext.GetSlotOccupancyRunIdsAsync(TestContext.Current.CancellationToken);

        // Assert
        ids.ShouldBeEmpty();
    }
}
