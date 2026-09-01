using Foundry.Modules.Issues.Contracts;
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

public sealed class OfTypeQueuedIssue : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;

    public OfTypeQueuedIssue()
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
    public async Task WhenMixedStatesExist_OfTypeQueuedIssueReturnsAllThreeQueuedTiersAndExcludesNonClaimable()
    {
        // Arrange
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        // Seed FreshQueuedIssue
        FreshQueuedIssue queued = new IssueBuilder()
            .WithIssueNumber(1)
            .WithTitle("Queued issue")
            .FreshQueued();
        _dbContext.Set<Issue>().Add(queued);

        // Seed RevisionQueuedIssue
        RevisionQueuedIssue revisionQueued = new IssueBuilder()
            .WithIssueNumber(2)
            .WithTitle("Revision queued issue")
            .WithBranchName("feat/issue-2")
            .WithPullRequestUrl("https://github.com/owner/repo/pull/2")
            .WithFeedbackCutoffAt(DateTimeOffset.UtcNow.AddDays(7))
            .WithReviewComments([new ReviewComment("Fix this.")])
            .RevisionQueued();
        _dbContext.Set<Issue>().Add(revisionQueued);

        // Seed ContinuationQueuedIssue
        ContinuationQueuedIssue continuationQueued = new IssueBuilder()
            .WithIssueNumber(3)
            .WithTitle("Continuation queued issue")
            .WithBranchName("feat/issue-3")
            .WithFailureReason("Container OOM")
            .WithFailureCategory("generic_failure")
            .ContinuableFailed()
            .Retry();
        _dbContext.Set<Issue>().Add(continuationQueued);

        // Seed InProgressIssue (non-claimable)
        InProgressIssue inProgress4 = new IssueBuilder()
            .WithIssueNumber(4)
            .WithTitle("In-progress issue — non-claimable")
            .InProgress();
        _dbContext.Set<Issue>().Add(inProgress4);

        await _dbContext.SaveChangesAsync(cancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        List<QueuedIssue> results = await _dbContext
            .Set<Issue>()
            .OfType<QueuedIssue>()
            .ToListAsync(cancellationToken);

        // Assert
        results.Count.ShouldBe(3);
        results.ShouldContain(i => i.Id == queued.Id);
        results.ShouldContain(i => i.Id == revisionQueued.Id);
        results.ShouldContain(i => i.Id == continuationQueued.Id);
        results.ShouldNotContain(i => i.Id == inProgress4.Id);
    }
}
