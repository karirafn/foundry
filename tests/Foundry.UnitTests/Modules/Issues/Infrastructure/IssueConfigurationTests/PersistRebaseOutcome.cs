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

public sealed class PersistRebaseOutcome : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;

    public PersistRebaseOutcome()
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
    public async Task WhenRebasePending_FieldsRoundTrip()
    {
        // Arrange
        DateTimeOffset feedbackCutoffAt = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);
        RebasePendingIssue rebasePending = new IssueBuilder()
            .WithIssueNumber(200)
            .WithBranchName("feat/200-rebase")
            .WithPullRequestUrl("https://github.com/owner/repo/pull/200")
            .WithFeedbackCutoffAt(feedbackCutoffAt)
            .RebasePending();

        _dbContext.Set<Issue>().Add(rebasePending);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        Issue? result = await _dbContext.Set<Issue>().FindAsync([rebasePending.Id], TestContext.Current.CancellationToken);

        // Assert
        RebasePendingIssue reloaded = result.ShouldBeOfType<RebasePendingIssue>();
        reloaded.ShouldSatisfyAllConditions(
            () => reloaded.WorkerRunId.ShouldBe(rebasePending.WorkerRunId),
            () => reloaded.BranchName.ShouldBe("feat/200-rebase"),
            () => reloaded.PullRequestUrl.ShouldBe("https://github.com/owner/repo/pull/200"),
            () => reloaded.FeedbackCutoffAt.ShouldBe(feedbackCutoffAt),
            () => reloaded.EnteredRebasePendingAt.ShouldBe(rebasePending.EnteredRebasePendingAt));
    }

    public static TheoryData<RebaseOutcome> Outcomes() =>
    [
        new RebaseOutcome.UpToDate(),
        new RebaseOutcome.Rebased(),
        new RebaseOutcome.Disabled(),
        new RebaseOutcome.Skipped("timeout"),
        new RebaseOutcome.Conflict(),
    ];

    [Theory]
    [MemberData(nameof(Outcomes))]
    public async Task WhenChecksPending_RebaseOutcomeRoundTrips(RebaseOutcome outcome)
    {
        // Arrange
        ChecksPendingIssue checksPending = new IssueBuilder().WithIssueNumber(201).WithRebaseOutcome(outcome).ChecksPending();
        _dbContext.Set<Issue>().Add(checksPending);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        Issue? result = await _dbContext.Set<Issue>().FindAsync([checksPending.Id], TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeOfType<ChecksPendingIssue>().RebaseOutcome.ShouldBe(outcome);
    }

    [Theory]
    [MemberData(nameof(Outcomes))]
    public async Task WhenReview_RebaseOutcomeRoundTrips(RebaseOutcome outcome)
    {
        // Arrange
        ReviewIssue review = new IssueBuilder().WithIssueNumber(202).WithRebaseOutcome(outcome).Review();
        _dbContext.Set<Issue>().Add(review);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        Issue? result = await _dbContext.Set<Issue>().FindAsync([review.Id], TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeOfType<ReviewIssue>().RebaseOutcome.ShouldBe(outcome);
    }
}
