using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Infrastructure.IssueConfigurationTests;

public sealed class PersistInProgressIssue : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;

    public PersistInProgressIssue()
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
    public async Task WhenInProgressIssueTransitioned_CanBeReloadedAsInProgressIssueWithWorkerRunId()
    {
        // Arrange
        IssueBuilder builder = new IssueBuilder()
            .WithIssueNumber(42)
            .WithTitle("In-progress issue")
            .WithLabels([]);
        InProgressIssue inProgress = builder.InProgress();

        _dbContext.Set<Issue>().Add(inProgress);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        Issue? result = await _dbContext
            .Set<Issue>()
            .FindAsync([inProgress.Id], TestContext.Current.CancellationToken);

        // Assert
        InProgressIssue reloaded = result.ShouldBeOfType<InProgressIssue>();
        reloaded.WorkerRunId.ShouldBe(inProgress.WorkerRunId);
    }
}
