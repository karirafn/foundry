using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Infrastructure.IssueConfigurationTests;

public sealed class PersistMarkedReadyPullRequestUrl : IAsyncDisposable
{
    private const string PullRequestUrl = "https://github.com/owner/repo/pull/1";

    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;

    public PersistMarkedReadyPullRequestUrl()
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
    public async Task WhenRecorded_RoundTripsMarked()
    {
        // Arrange
        ReviewIssue review = new IssueBuilder().WithPullRequestUrl(PullRequestUrl).Review();
        review.RecordPullRequestMarkedReady(PullRequestUrl);

        // Act
        Issue reloaded = await RoundTripAsync(review);

        // Assert
        reloaded.MarkedReadyPullRequestUrl.ShouldBe(PullRequestUrl);
        reloaded.IsPullRequestMarkedReady.ShouldBeTrue();
    }

    [Fact]
    public async Task WhenNeverRecorded_RoundTripsUnmarked()
    {
        // Act
        Issue reloaded = await RoundTripAsync(new IssueBuilder().WithPullRequestUrl(PullRequestUrl).Review());

        // Assert
        reloaded.MarkedReadyPullRequestUrl.ShouldBeNull();
        reloaded.IsPullRequestMarkedReady.ShouldBeFalse();
    }
}
