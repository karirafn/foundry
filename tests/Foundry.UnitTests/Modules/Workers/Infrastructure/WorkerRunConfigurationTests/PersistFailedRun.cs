using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Domain;
using Foundry.Shared;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Infrastructure.WorkerRunConfigurationTests;

public sealed class PersistFailedRun : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;

    public PersistFailedRun()
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
    public async Task WhenFailedRunWithNonZeroExit_RoundTripsCorrectly()
    {
        // Arrange
        IssueId issueId = IssueId.New();
        StartingRun starting = StartingRun.Begin(issueId, WorkerRunId.New());
        FailedRun run = starting.Fail(new FailureReason.NonZeroExit(42));

        _dbContext.Set<WorkerRun>().Add(run);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        WorkerRun? result = await _dbContext
            .Set<WorkerRun>()
            .FindAsync([run.Id], TestContext.Current.CancellationToken);

        // Assert
        FailedRun reloaded = result.ShouldBeOfType<FailedRun>();
        FailureReason.NonZeroExit reason = reloaded.Reason.ShouldBeOfType<FailureReason.NonZeroExit>();
        reloaded.ShouldSatisfyAllConditions(
            () => reloaded.Id.ShouldBe(run.Id),
            () => reloaded.IssueId.ShouldBe(issueId),
            () => reloaded.FailedAt.ShouldBe(run.FailedAt),
            () => reason.ExitCode.ShouldBe(42));
    }

    [Fact]
    public async Task WhenFailedRunWithTimedOut_RoundTripsCorrectly()
    {
        // Arrange
        IssueId issueId = IssueId.New();
        StartingRun starting = StartingRun.Begin(issueId, WorkerRunId.New());
        FailedRun run = starting.Fail(new FailureReason.TimedOut());

        _dbContext.Set<WorkerRun>().Add(run);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        WorkerRun? result = await _dbContext
            .Set<WorkerRun>()
            .FindAsync([run.Id], TestContext.Current.CancellationToken);

        // Assert
        FailedRun reloaded = result.ShouldBeOfType<FailedRun>();
        reloaded.Reason.ShouldBeOfType<FailureReason.TimedOut>();
    }

    [Fact]
    public async Task WhenFailedRunWithContainerError_RoundTripsCorrectly()
    {
        // Arrange
        IssueId issueId = IssueId.New();
        StartingRun starting = StartingRun.Begin(issueId, WorkerRunId.New());
        FailedRun run = starting.Fail(new FailureReason.ContainerError("Docker daemon unavailable"));

        _dbContext.Set<WorkerRun>().Add(run);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        WorkerRun? result = await _dbContext
            .Set<WorkerRun>()
            .FindAsync([run.Id], TestContext.Current.CancellationToken);

        // Assert
        FailedRun reloaded = result.ShouldBeOfType<FailedRun>();
        FailureReason.ContainerError reason = reloaded.Reason.ShouldBeOfType<FailureReason.ContainerError>();
        reason.Message.ShouldBe("Docker daemon unavailable");
    }

    [Fact]
    public async Task WhenFailedRunFromActiveWithBranchName_PropagatesBranchName()
    {
        // Arrange
        IssueId issueId = IssueId.New();
        StartingRun starting = StartingRun.Begin(issueId, WorkerRunId.New());
        ActiveRun active = starting.Activate(
            ContainerId.From("container-with-branch"),
            BranchName.From("feat/reported-branch"),
            MonitoredRepositoryId.New());
        FailedRun run = active.Fail(new FailureReason.NonZeroExit(1));

        _dbContext.Set<WorkerRun>().Add(run);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        WorkerRun? result = await _dbContext
            .Set<WorkerRun>()
            .FindAsync([run.Id], TestContext.Current.CancellationToken);

        // Assert
        FailedRun reloaded = result.ShouldBeOfType<FailedRun>();
        reloaded.BranchName.ShouldBe(BranchName.From("feat/reported-branch"));
    }
}
