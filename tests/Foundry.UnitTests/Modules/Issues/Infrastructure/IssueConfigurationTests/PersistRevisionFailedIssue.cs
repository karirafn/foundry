using Foundry.Modules.Issues.Domain;
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

public sealed class PersistRevisionFailedIssue : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;

    public PersistRevisionFailedIssue()
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

    [Fact]
    public async Task WhenRevisionFailed_CanBeReloadedWithAllFields()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        DetectedIssue detected = DetectedIssue.Detect(
            repositoryId,
            issueNumber: 61,
            title: "Revision failed issue",
            body: "Body",
            author: ValidAuthor,
            url: ValidUrl,
            labels: [],
            detectedAt: DateTimeOffset.UtcNow);

        _dbContext.Set<Issue>().Add(detected);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        QueuedIssue queued = detected.Enqueue();
        await _dbContext.TransitionAsync(detected, queued, new NullDomainEventDispatcher(), TestContext.Current.CancellationToken);

        InProgressIssue inProgress = queued.Claim(Guid.NewGuid());
        await _dbContext.TransitionAsync(queued, inProgress, new NullDomainEventDispatcher(), TestContext.Current.CancellationToken);

        ReviewIssue review = inProgress.MarkInReview(
            Guid.NewGuid(),
            "feat/issue-61",
            "https://github.com/owner/repo/pull/21",
            DateTimeOffset.UtcNow);
        await _dbContext.TransitionAsync(inProgress, review, new NullDomainEventDispatcher(), TestContext.Current.CancellationToken);

        IReadOnlyList<ReviewComment> comments =
        [
            new ReviewComment("Please fix the formatting."),
            new ReviewComment("Rename this variable.", "src/Foo.cs", 42),
        ];
        RevisionQueuedIssue revisionQueued = review.Revise(comments);
        await _dbContext.TransitionAsync(review, revisionQueued, new NullDomainEventDispatcher(), TestContext.Current.CancellationToken);

        Guid workerRunId = Guid.NewGuid();
        RevisionInProgressIssue revisionInProgress = revisionQueued.Claim(workerRunId);
        await _dbContext.TransitionAsync(revisionQueued, revisionInProgress, new NullDomainEventDispatcher(), TestContext.Current.CancellationToken);

        DateTimeOffset failedAt = new DateTimeOffset(2026, 6, 1, 15, 0, 0, TimeSpan.Zero);
        RevisionFailedIssue revisionFailed = revisionInProgress.MarkFailed(
            workerRunId,
            "Container exited with code 1",
            "generic_failure",
            failedAt);
        await _dbContext.TransitionAsync(revisionInProgress, revisionFailed, new NullDomainEventDispatcher(), TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        Issue? result = await _dbContext
            .Set<Issue>()
            .FindAsync([revisionFailed.Id], TestContext.Current.CancellationToken);

        // Assert
        RevisionFailedIssue reloaded = result.ShouldBeOfType<RevisionFailedIssue>();
        reloaded.ShouldSatisfyAllConditions(
            () => reloaded.WorkerRunId.ShouldBe(workerRunId),
            () => reloaded.BranchName.ShouldBe("feat/issue-61"),
            () => reloaded.PullRequestUrl.ShouldBe("https://github.com/owner/repo/pull/21"),
            () => reloaded.ReviewComments.Count.ShouldBe(2),
            () => reloaded.ReviewComments[0].Body.ShouldBe("Please fix the formatting."),
            () => reloaded.ReviewComments[1].Body.ShouldBe("Rename this variable."),
            () => reloaded.ReviewComments[1].FilePath.ShouldBe("src/Foo.cs"),
            () => reloaded.ReviewComments[1].Line.ShouldBe(42),
            () => reloaded.FailureReason.ShouldBe("Container exited with code 1"),
            () => reloaded.FailedAt.ShouldBe(failedAt),
            () => reloaded.Author.Value.ShouldBe(ValidAuthor.Value),
            () => reloaded.MonitoredRepositoryId.ShouldBe(repositoryId));
    }
}
