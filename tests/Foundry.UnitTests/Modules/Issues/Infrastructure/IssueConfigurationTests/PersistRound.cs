using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Infrastructure.IssueConfigurationTests;

public sealed class PersistRound : IAsyncDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;

    public PersistRound()
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

    private async Task<Issue> RoundTripAsync(Issue issue)
    {
        _dbContext.Set<Issue>().Add(issue);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();
        return (await _dbContext.Set<Issue>().FindAsync([issue.Id], TestContext.Current.CancellationToken)).ShouldNotBeNull();
    }

    [Fact]
    public async Task WhenNeverRevised_RoundTripsRoundOne()
    {
        // Act
        Issue reloaded = await RoundTripAsync(new IssueBuilder().Detected());

        // Assert
        reloaded.Round.ShouldBe(1);
    }

    [Fact]
    public async Task WhenRevisedTwice_RoundTripsRoundThree()
    {
        // Arrange
        RevisionQueuedIssue revisionQueued = new IssueBuilder()
            .RevisionInProgress()
            .MarkRebasePending(Now, Now)
            .ResolveConflict(Now);

        // Act
        Issue reloaded = await RoundTripAsync(revisionQueued);

        // Assert
        reloaded.Round.ShouldBe(3);
    }
}
