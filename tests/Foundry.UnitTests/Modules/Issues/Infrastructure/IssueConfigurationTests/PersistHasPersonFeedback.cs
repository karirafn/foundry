using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Infrastructure.IssueConfigurationTests;

public sealed class PersistHasPersonFeedback : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;

    public PersistHasPersonFeedback()
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
    public async Task WhenSetByReviewFeedback_RoundTripsTrue()
    {
        // Arrange
        RevisionQueuedIssue revisionQueued = new IssueBuilder()
            .WithReviewComments([new ReviewComment("Please rename this.")])
            .RevisionQueued();

        // Act
        Issue reloaded = await RoundTripAsync(revisionQueued);

        // Assert
        reloaded.HasPersonFeedback.ShouldBeTrue();
    }

    [Fact]
    public async Task WhenNeverSet_RoundTripsFalse()
    {
        // Act
        Issue reloaded = await RoundTripAsync(new IssueBuilder().ChecksPending());

        // Assert
        reloaded.HasPersonFeedback.ShouldBeFalse();
    }
}
