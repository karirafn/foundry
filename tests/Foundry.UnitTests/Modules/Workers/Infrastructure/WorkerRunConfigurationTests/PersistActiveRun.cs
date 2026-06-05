using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Workers.Domain;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Infrastructure.WorkerRunConfigurationTests;

public sealed class PersistActiveRun : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;

    public PersistActiveRun()
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
    public async Task WhenActiveRunPersisted_CanBeReloadedAsActiveRunWithAllProperties()
    {
        // Arrange
        IssueId issueId = IssueId.New();
        StartingRun starting = StartingRun.Begin(issueId, WorkerRunId.New());
        ActiveRun run = starting.Activate(ContainerId.From("container-abc123"));

        _dbContext.Set<WorkerRun>().Add(run);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        WorkerRun? result = await _dbContext
            .Set<WorkerRun>()
            .FindAsync([run.Id], TestContext.Current.CancellationToken);

        // Assert
        ActiveRun reloaded = result.ShouldBeOfType<ActiveRun>();
        reloaded.ShouldSatisfyAllConditions(
            () => reloaded.Id.ShouldBe(run.Id),
            () => reloaded.IssueId.ShouldBe(issueId),
            () => reloaded.ContainerId.ShouldBe(ContainerId.From("container-abc123")),
            () => reloaded.StartedAt.ShouldBe(run.StartedAt),
            () => reloaded.LatestProgress.ShouldBeNull());
    }

    [Fact]
    public async Task WhenActiveRunWithNullBranchName_RoundTripsWithNullBranchName()
    {
        // Arrange
        IssueId issueId = IssueId.New();
        StartingRun starting = StartingRun.Begin(issueId, WorkerRunId.New());
        ActiveRun run = starting.Activate(ContainerId.From("container-no-branch"));

        _dbContext.Set<WorkerRun>().Add(run);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        WorkerRun? result = await _dbContext
            .Set<WorkerRun>()
            .FindAsync([run.Id], TestContext.Current.CancellationToken);

        // Assert
        ActiveRun reloaded = result.ShouldBeOfType<ActiveRun>();
        reloaded.BranchName.ShouldBeNull();
    }

    [Fact]
    public async Task WhenActiveRunWithBranchNameSet_RoundTripsBranchName()
    {
        // Arrange
        IssueId issueId = IssueId.New();
        StartingRun starting = StartingRun.Begin(issueId, WorkerRunId.New());
        ActiveRun run = starting.Activate(ContainerId.From("container-with-branch"));
        run.SetBranchName(BranchName.From("feat/my-feature"));

        _dbContext.Set<WorkerRun>().Add(run);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        WorkerRun? result = await _dbContext
            .Set<WorkerRun>()
            .FindAsync([run.Id], TestContext.Current.CancellationToken);

        // Assert
        ActiveRun reloaded = result.ShouldBeOfType<ActiveRun>();
        reloaded.BranchName.ShouldBe(BranchName.From("feat/my-feature"));
    }

    [Fact]
    public async Task WhenActiveRunWithProgressPersisted_CanBeReloadedWithProgress()
    {
        // Arrange
        IssueId issueId = IssueId.New();
        StartingRun starting = StartingRun.Begin(issueId, WorkerRunId.New());
        ActiveRun run = starting.Activate(ContainerId.From("container-xyz"));
        run.UpdateProgress("Step 1 complete");

        _dbContext.Set<WorkerRun>().Add(run);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        WorkerRun? result = await _dbContext
            .Set<WorkerRun>()
            .FindAsync([run.Id], TestContext.Current.CancellationToken);

        // Assert
        ActiveRun reloaded = result.ShouldBeOfType<ActiveRun>();
        reloaded.LatestProgress.ShouldBe("Step 1 complete");
    }
}
