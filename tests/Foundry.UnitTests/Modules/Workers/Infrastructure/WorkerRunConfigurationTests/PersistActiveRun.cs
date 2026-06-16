using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
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
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        StartingRun starting = StartingRun.Begin(issueId, WorkerRunId.New());
        BranchName branchName = BranchName.From("feat/42-my-feature");
        ActiveRun run = starting.Activate(ContainerId.From("container-abc123"), branchName, repositoryId);

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
            () => reloaded.BranchName.ShouldBe(branchName),
            () => reloaded.MonitoredRepositoryId.ShouldBe(repositoryId));
    }
}
