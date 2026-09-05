using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Infrastructure.IssueConfigurationTests;

public sealed class PersistCompletedIssue : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;

    public PersistCompletedIssue()
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
    public async Task WhenCompletedFromReview_CanBeReloadedAsCompletedIssueWithPrFields()
    {
        // Arrange
        DateTimeOffset completedAt = new DateTimeOffset(2026, 5, 30, 15, 0, 0, TimeSpan.Zero);
        CompletedIssue completed = new IssueBuilder()
            .WithIssueNumber(46)
            .WithTitle("Issue 46")
            .WithLabels([])
            .WithBranchName("feat/issue-46")
            .WithPullRequestUrl("https://github.com/owner/repo/pull/2")
            .WithCompletedAt(completedAt)
            .Completed();

        _dbContext.Set<Issue>().Add(completed);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        Issue? result = await _dbContext
            .Set<Issue>()
            .FindAsync([completed.Id], TestContext.Current.CancellationToken);

        // Assert
        CompletedIssue reloaded = result.ShouldBeOfType<CompletedIssue>();
        reloaded.ShouldSatisfyAllConditions(
            () => reloaded.CompletedAt.ShouldBe(completedAt),
            () => reloaded.BranchName.ShouldBe("feat/issue-46"),
            () => reloaded.PullRequestUrl.ShouldBe("https://github.com/owner/repo/pull/2"),
            () => reloaded.Author.Value.ShouldBe("octocat"),
            () => reloaded.Url.Value.ShouldBe(new Uri("https://github.com/owner/repo/issues/1")));
    }
}
