using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Infrastructure.IssueConfigurationTests;

public sealed class PersistBlockedIssue : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;

    public PersistBlockedIssue()
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
    public async Task WhenBlockedIssueTransitioned_CanBeReloadedAsBlockedIssueWithBlockedBy()
    {
        // Arrange
        IReadOnlyList<int> blockers = [7, 13];
        BlockedIssue blocked = new IssueBuilder()
            .WithIssueNumber(3)
            .WithTitle("Blocked issue")
            .WithBody("Blocked body")
            .WithLabels([])
            .Detected()
            .Block(blockers);

        _dbContext.Set<Issue>().Add(blocked);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        Issue? result = await _dbContext
            .Set<Issue>()
            .FindAsync([blocked.Id], TestContext.Current.CancellationToken);

        // Assert
        BlockedIssue reloaded = result.ShouldBeOfType<BlockedIssue>();
        reloaded.BlockedBy.ShouldBe(blockers);
    }
}
