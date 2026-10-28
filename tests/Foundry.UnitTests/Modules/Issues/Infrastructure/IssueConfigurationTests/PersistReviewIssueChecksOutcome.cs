using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Infrastructure.IssueConfigurationTests;

public sealed class PersistReviewIssueChecksOutcome : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;

    public PersistReviewIssueChecksOutcome()
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
    public async Task WhenReviewHasPassedOutcome_ChecksOutcomeRoundTrips()
    {
        // Arrange
        ReviewIssue review = new IssueBuilder()
            .WithIssueNumber(100)
            .WithTitle("Review with passed outcome")
            .WithChecksOutcome(ChecksOutcomeBuilder.Passed())
            .ReviewFromChecksPending();

        _dbContext.Set<Issue>().Add(review);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        Issue? result = await _dbContext
            .Set<Issue>()
            .FindAsync([review.Id], TestContext.Current.CancellationToken);

        // Assert
        ReviewIssue reloaded = result.ShouldBeOfType<ReviewIssue>();
        reloaded.ChecksOutcome.ShouldBeOfType<ChecksOutcome.Passed>();
    }

    [Fact]
    public async Task WhenReviewHasNoneDetectedOutcome_ChecksOutcomeRoundTrips()
    {
        // Arrange
        ReviewIssue review = new IssueBuilder()
            .WithIssueNumber(101)
            .WithTitle("Review with none detected outcome")
            .WithChecksOutcome(ChecksOutcomeBuilder.NoneDetected())
            .ReviewFromChecksPending();

        _dbContext.Set<Issue>().Add(review);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        Issue? result = await _dbContext
            .Set<Issue>()
            .FindAsync([review.Id], TestContext.Current.CancellationToken);

        // Assert
        ReviewIssue reloaded = result.ShouldBeOfType<ReviewIssue>();
        reloaded.ChecksOutcome.ShouldBeOfType<ChecksOutcome.NoneDetected>();
    }

    [Fact]
    public async Task WhenReviewHasUnfinishedOutcome_ChecksOutcomeRoundTrips()
    {
        // Arrange
        ReviewIssue review = new IssueBuilder()
            .WithIssueNumber(102)
            .WithTitle("Review with unfinished outcome")
            .WithChecksOutcome(ChecksOutcomeBuilder.Unfinished())
            .ReviewFromChecksPending();

        _dbContext.Set<Issue>().Add(review);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        Issue? result = await _dbContext
            .Set<Issue>()
            .FindAsync([review.Id], TestContext.Current.CancellationToken);

        // Assert
        ReviewIssue reloaded = result.ShouldBeOfType<ReviewIssue>();
        reloaded.ChecksOutcome.ShouldBeOfType<ChecksOutcome.Unfinished>();
    }

    [Fact]
    public async Task WhenReviewHasStillFailingOutcome_ChecksOutcomeRoundTripsWithFailingChecks()
    {
        // Arrange
        ChecksOutcome.StillFailing outcome = new ChecksOutcomeBuilder()
            .WithFailingChecks([
                new FailingCheck("ci/build", "failure", "https://github.com/owner/repo/runs/1"),
                new FailingCheck("ci/lint", "failure", null),
            ])
            .StillFailing();

        ReviewIssue review = new IssueBuilder()
            .WithIssueNumber(103)
            .WithTitle("Review with still failing outcome")
            .WithChecksOutcome(outcome)
            .ReviewFromChecksPending();

        _dbContext.Set<Issue>().Add(review);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        Issue? result = await _dbContext
            .Set<Issue>()
            .FindAsync([review.Id], TestContext.Current.CancellationToken);

        // Assert
        ReviewIssue reloaded = result.ShouldBeOfType<ReviewIssue>();
        ChecksOutcome.StillFailing reloadedOutcome = reloaded.ChecksOutcome.ShouldBeOfType<ChecksOutcome.StillFailing>();
        reloadedOutcome.ShouldSatisfyAllConditions(
            () => reloadedOutcome.FailingChecks.Count.ShouldBe(2),
            () => reloadedOutcome.FailingChecks[0].Name.ShouldBe("ci/build"),
            () => reloadedOutcome.FailingChecks[0].Conclusion.ShouldBe("failure"),
            () => reloadedOutcome.FailingChecks[0].DetailsUrl.ShouldBe("https://github.com/owner/repo/runs/1"),
            () => reloadedOutcome.FailingChecks[1].Name.ShouldBe("ci/lint"),
            () => reloadedOutcome.FailingChecks[1].DetailsUrl.ShouldBeNull());
    }

    [Fact]
    public async Task WhenReviewHasUnreadableOutcome_ChecksOutcomeRoundTrips()
    {
        // Arrange
        ReviewIssue review = new IssueBuilder()
            .WithIssueNumber(104)
            .WithTitle("Review with unreadable outcome")
            .WithChecksOutcome(ChecksOutcomeBuilder.Unreadable())
            .ReviewFromChecksPending();

        _dbContext.Set<Issue>().Add(review);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        Issue? result = await _dbContext
            .Set<Issue>()
            .FindAsync([review.Id], TestContext.Current.CancellationToken);

        // Assert
        ReviewIssue reloaded = result.ShouldBeOfType<ReviewIssue>();
        reloaded.ChecksOutcome.ShouldBeOfType<ChecksOutcome.Unreadable>();
    }

    [Fact]
    public async Task WhenReviewCreatedFromInProgress_ChecksOutcomeDefaultsToPassedAndRoundTrips()
    {
        // Arrange
        ReviewIssue review = new IssueBuilder()
            .WithIssueNumber(105)
            .WithTitle("Review created from in-progress")
            .Review();

        _dbContext.Set<Issue>().Add(review);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        Issue? result = await _dbContext
            .Set<Issue>()
            .FindAsync([review.Id], TestContext.Current.CancellationToken);

        // Assert
        ReviewIssue reloaded = result.ShouldBeOfType<ReviewIssue>();
        reloaded.ChecksOutcome.ShouldBeOfType<ChecksOutcome.Passed>();
    }
}
