using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Features.ProviderReactions;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Features.ProviderReactions.ProviderPullRequestClosedHandlerTests;

/// <summary>The pull request closed without merging while the review step held the issue (ADR 0113).</summary>
public sealed class ReviewStep : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly ProviderPullRequestClosedHandler _sut;

    public ReviewStep()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new FoundryDbContext(options);
        _dbContext.Database.EnsureCreated();
        _sut = new ProviderPullRequestClosedHandler(
            _dbContext,
            new CapturingDomainEventDispatcher(),
            NullLogger<ProviderPullRequestClosedHandler>.Instance);
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    public static TheoryData<string> ReviewStepStates() => ["review_queued", "review_in_progress", "review_failed"];

    private Issue Seed(string state, MonitoredRepositoryId repositoryId)
    {
        IssueBuilder builder = new IssueBuilder().WithMonitoredRepositoryId(repositoryId).WithIssueNumber(7);
        Issue issue = state switch
        {
            "review_queued" => builder.ReviewQueued(),
            "review_in_progress" => builder.ReviewInProgress(),
            "review_failed" => builder.ReviewFailed(),
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
        };
        _dbContext.Set<Issue>().Add(issue);
        _dbContext.SaveChanges();
        _dbContext.ChangeTracker.Clear();
        return issue;
    }

    [Theory]
    [MemberData(nameof(ReviewStepStates))]
    public async Task WhenThePullRequestClosesUnmergedInAReviewStepState_TheIssueFailsContinuably(string state)
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        Issue issue = Seed(state, repositoryId);

        // Act
        await _sut.HandleAsync(new ProviderPullRequestClosed(repositoryId, 7), TestContext.Current.CancellationToken);

        // Assert
        _dbContext.ChangeTracker.Clear();
        Issue? reloaded = await _dbContext.Set<Issue>().FindAsync([issue.Id], TestContext.Current.CancellationToken);
        ContinuableFailedIssue failed = reloaded.ShouldBeOfType<ContinuableFailedIssue>();
        failed.ShouldSatisfyAllConditions(
            () => failed.FailureCategory.ShouldBe(FailureCategory.PrClosed),
            () => failed.BranchName.ShouldBe("feat/1-test"));
    }
}
