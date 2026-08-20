using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Infrastructure.IssueConfigurationTests;

public sealed class OfTypeClaimableIssue : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;

    public OfTypeClaimableIssue()
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

    private static IssueAuthor ValidAuthor =>
        IssueAuthor.Create("octocat").ValueOrThrow();

    private static ProviderUrl ValidUrl =>
        ProviderUrl.Create("https://github.com/owner/repo/issues/1").ValueOrThrow();

    private DetectedIssue SeedDetected(int issueNumber, string title)
    {
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        DetectedIssue detected = DetectedIssue.Detect(
            repositoryId,
            issueNumber,
            title,
            body: "Body",
            author: ValidAuthor,
            url: ValidUrl,
            labels: [],
            detectedAt: DateTimeOffset.UtcNow);

        _dbContext.Set<Issue>().Add(detected);
        return detected;
    }

    [Fact]
    public async Task WhenMixedStatesExist_OfTypeClaimableIssueReturnsAllThreeQueuedTiersAndExcludesNonClaimable()
    {
        // Arrange
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        NullDomainEventDispatcher dispatcher = new();

        DetectedIssue detected1 = SeedDetected(issueNumber: 1, title: "Queued issue");
        DetectedIssue detected2 = SeedDetected(issueNumber: 2, title: "Revision queued issue");
        DetectedIssue detected3 = SeedDetected(issueNumber: 3, title: "Continuation queued issue");
        DetectedIssue detected4 = SeedDetected(issueNumber: 4, title: "In-progress issue — non-claimable");
        await _dbContext.SaveChangesAsync(cancellationToken);

        // Seed QueuedIssue
        QueuedIssue queued = detected1.Enqueue();
        await _dbContext.TransitionAsync(detected1, queued, dispatcher, cancellationToken);

        // Seed RevisionQueuedIssue
        QueuedIssue queued2 = detected2.Enqueue();
        await _dbContext.TransitionAsync(detected2, queued2, dispatcher, cancellationToken);
        InProgressIssue inProgress2 = queued2.Claim(Guid.NewGuid());
        await _dbContext.TransitionAsync(queued2, inProgress2, dispatcher, cancellationToken);
        ReviewIssue review2 = inProgress2.MarkInReview(
            Guid.NewGuid(),
            "feat/issue-2",
            "https://github.com/owner/repo/pull/2",
            feedbackCutoffAt: DateTimeOffset.UtcNow.AddDays(7));
        await _dbContext.TransitionAsync(inProgress2, review2, dispatcher, cancellationToken);
        RevisionQueuedIssue revisionQueued = review2.Revise([new ReviewComment("Fix this.")]);
        await _dbContext.TransitionAsync(review2, revisionQueued, dispatcher, cancellationToken);

        // Seed ContinuationQueuedIssue
        QueuedIssue queued3 = detected3.Enqueue();
        await _dbContext.TransitionAsync(detected3, queued3, dispatcher, cancellationToken);
        InProgressIssue inProgress3 = queued3.Claim(Guid.NewGuid());
        await _dbContext.TransitionAsync(queued3, inProgress3, dispatcher, cancellationToken);
        ContinuableFailedIssue continuableFailed = inProgress3.MarkContinuableFailed(
            Guid.NewGuid(),
            "feat/issue-3",
            "Container OOM",
            "generic_failure",
            failedAt: DateTimeOffset.UtcNow);
        await _dbContext.TransitionAsync(inProgress3, continuableFailed, dispatcher, cancellationToken);
        ContinuationQueuedIssue continuationQueued = continuableFailed.Retry();
        await _dbContext.TransitionAsync(continuableFailed, continuationQueued, dispatcher, cancellationToken);

        // Seed InProgressIssue (non-claimable)
        QueuedIssue queued4 = detected4.Enqueue();
        await _dbContext.TransitionAsync(detected4, queued4, dispatcher, cancellationToken);
        InProgressIssue inProgress4 = queued4.Claim(Guid.NewGuid());
        await _dbContext.TransitionAsync(queued4, inProgress4, dispatcher, cancellationToken);

        _dbContext.ChangeTracker.Clear();

        // Act
        List<ClaimableIssue> results = await _dbContext
            .Set<Issue>()
            .OfType<ClaimableIssue>()
            .ToListAsync(cancellationToken);

        // Assert
        results.Count.ShouldBe(3);
        results.ShouldContain(i => i.Id == queued.Id);
        results.ShouldContain(i => i.Id == revisionQueued.Id);
        results.ShouldContain(i => i.Id == continuationQueued.Id);
        results.ShouldNotContain(i => i.Id == inProgress4.Id);
    }
}
