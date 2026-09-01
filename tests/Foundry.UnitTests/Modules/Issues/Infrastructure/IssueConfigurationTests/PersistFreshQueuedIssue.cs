using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Infrastructure.IssueConfigurationTests;

public sealed class PersistFreshQueuedIssue : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;

    public PersistFreshQueuedIssue()
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
    public async Task WhenFreshQueuedIssueTransitioned_CanBeReloadedAsFreshQueuedIssue()
    {
        // Arrange
        FreshQueuedIssue queued = new IssueBuilder()
            .WithIssueNumber(7)
            .WithTitle("Queue me")
            .WithBody("Queue body")
            .WithLabels([])
            .FreshQueued();

        _dbContext.Set<Issue>().Add(queued);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        Issue? result = await _dbContext
            .Set<Issue>()
            .FindAsync([queued.Id], TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeOfType<FreshQueuedIssue>();
    }
}
