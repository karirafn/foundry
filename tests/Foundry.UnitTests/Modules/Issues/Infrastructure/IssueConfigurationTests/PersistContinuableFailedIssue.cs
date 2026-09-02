using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Infrastructure.IssueConfigurationTests;

public sealed class PersistContinuableFailedIssue : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;

    public PersistContinuableFailedIssue()
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
    public async Task WhenInProgressMarkedContinuableFailed_CanBeReloadedWithAllFields()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        WorkerRunId workerRunId = WorkerRunId.New();
        DateTimeOffset failedAt = new DateTimeOffset(2026, 6, 9, 10, 0, 0, TimeSpan.Zero);
        ContinuableFailedIssue continuableFailed = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(71)
            .WithTitle("Continuable failed issue")
            .WithBody("Issue body")
            .WithLabels([])
            .WithWorkerRunId(workerRunId)
            .WithBranchName("feat/issue-71")
            .WithFailureReason("Tests timed out")
            .WithFailureCategory(FailureCategory.NonZeroExit)
            .WithFailedAt(failedAt)
            .ContinuableFailed();

        _dbContext.Set<Issue>().Add(continuableFailed);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        Issue? result = await _dbContext
            .Set<Issue>()
            .FindAsync([continuableFailed.Id], TestContext.Current.CancellationToken);

        // Assert
        ContinuableFailedIssue reloaded = result.ShouldBeOfType<ContinuableFailedIssue>();
        reloaded.ShouldSatisfyAllConditions(
            () => reloaded.WorkerRunId.ShouldBe(workerRunId),
            () => reloaded.BranchName.ShouldBe("feat/issue-71"),
            () => reloaded.PullRequestUrl.ShouldBe(string.Empty),
            () => reloaded.FailureReason.ShouldBe("Tests timed out"),
            () => reloaded.FailedAt.ShouldBe(failedAt),
            () => reloaded.Author.Value.ShouldBe("octocat"),
            () => reloaded.MonitoredRepositoryId.ShouldBe(repositoryId));
    }
}
