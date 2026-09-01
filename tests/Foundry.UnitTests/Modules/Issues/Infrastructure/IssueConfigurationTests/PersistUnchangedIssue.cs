using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Infrastructure.IssueConfigurationTests;

public sealed class PersistUnchangedIssue : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;

    public PersistUnchangedIssue()
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
    public async Task WhenUnchangedIssueTransitioned_CanBeReloadedAsUnchangedIssueWithWorkerRunId()
    {
        // Arrange
        Guid unchangedWorkerRunId = Guid.NewGuid();
        UnchangedIssue unchanged = new IssueBuilder()
            .WithIssueNumber(43)
            .WithTitle("Unchanged issue")
            .WithBody("Unchanged body")
            .WithLabels([])
            .WithWorkerRunId(unchangedWorkerRunId)
            .Unchanged();

        _dbContext.Set<Issue>().Add(unchanged);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        Issue? result = await _dbContext
            .Set<Issue>()
            .FindAsync([unchanged.Id], TestContext.Current.CancellationToken);

        // Assert
        UnchangedIssue reloaded = result.ShouldBeOfType<UnchangedIssue>();
        reloaded.ShouldSatisfyAllConditions(
            () => reloaded.WorkerRunId.ShouldBe(unchangedWorkerRunId),
            () => reloaded.Author.Value.ShouldBe("octocat"),
            () => reloaded.Url.Value.ShouldBe(new Uri("https://github.com/owner/repo/issues/1")));
    }
}
