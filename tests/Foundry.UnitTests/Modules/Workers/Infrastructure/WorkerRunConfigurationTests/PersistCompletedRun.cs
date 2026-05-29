using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Workers.Domain;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Infrastructure.WorkerRunConfigurationTests;

public sealed class PersistCompletedRun : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;

    public PersistCompletedRun()
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
    public async Task WhenCompletedRunPersisted_CanBeReloadedWithAllProperties()
    {
        // Arrange
        IssueId issueId = IssueId.New();
        StartingRun starting = StartingRun.Begin(issueId, WorkerRunId.New());
        ActiveRun active = starting.Activate(ContainerId.From("container-completed"));
        CompletedRun run = active.Complete(
            exitCode: 0,
            branchName: BranchName.From("feat/my-feature"),
            pullRequestUrl: PullRequestUrl.From("https://github.com/owner/repo/pull/1"));

        _dbContext.Set<WorkerRun>().Add(run);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        WorkerRun? result = await _dbContext
            .Set<WorkerRun>()
            .FindAsync([run.Id], TestContext.Current.CancellationToken);

        // Assert
        CompletedRun reloaded = result.ShouldBeOfType<CompletedRun>();
        reloaded.ShouldSatisfyAllConditions(
            () => reloaded.Id.ShouldBe(run.Id),
            () => reloaded.IssueId.ShouldBe(issueId),
            () => reloaded.ExitCode.ShouldBe(0),
            () => reloaded.CompletedAt.ShouldBe(run.CompletedAt),
            () => reloaded.BranchName.ShouldBe(BranchName.From("feat/my-feature")),
            () => reloaded.PullRequestUrl.ShouldBe(PullRequestUrl.From("https://github.com/owner/repo/pull/1")));
    }

    [Fact]
    public async Task WhenCompletedRunPersistedWithNullBranchAndPr_ReloadsWithNulls()
    {
        // Arrange
        IssueId issueId = IssueId.New();
        StartingRun starting = StartingRun.Begin(issueId, WorkerRunId.New());
        ActiveRun active = starting.Activate(ContainerId.From("container-no-branch"));
        CompletedRun run = active.Complete(exitCode: 1, branchName: null, pullRequestUrl: null);

        _dbContext.Set<WorkerRun>().Add(run);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        WorkerRun? result = await _dbContext
            .Set<WorkerRun>()
            .FindAsync([run.Id], TestContext.Current.CancellationToken);

        // Assert
        CompletedRun reloaded = result.ShouldBeOfType<CompletedRun>();
        reloaded.ShouldSatisfyAllConditions(
            () => reloaded.BranchName.ShouldBeNull(),
            () => reloaded.PullRequestUrl.ShouldBeNull());
    }
}
