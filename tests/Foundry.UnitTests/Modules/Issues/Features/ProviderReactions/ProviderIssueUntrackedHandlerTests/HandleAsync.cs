using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Issues.Features.ProviderReactions;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Features.ProviderReactions.ProviderIssueUntrackedHandlerTests;

public sealed class HandleAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly IIntegrationEventHandler<ProviderIssueUntracked> _sut;

    private static readonly DateTimeOffset Now = new(2026, 5, 27, 12, 0, 0, TimeSpan.Zero);

    private static IssueAuthor ValidAuthor =>
        IssueAuthor.Create("octocat").ValueOrThrow();

    private static ProviderUrl ValidUrl =>
        ProviderUrl.Create("https://github.com/owner/repo/issues/1").ValueOrThrow();

    public HandleAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new FoundryDbContext(options);
        _dbContext.Database.EnsureCreated();
        _sut = new ProviderIssueUntrackedHandler(
            _dbContext,
            NullLogger<ProviderIssueUntrackedHandler>.Instance);
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private DetectedIssue SeedDetectedIssue(MonitoredRepositoryId repositoryId, int issueNumber)
    {
        DetectedIssue detected = DetectedIssue.Detect(
            repositoryId,
            issueNumber: issueNumber,
            title: "Issue title",
            body: "Body",
            author: ValidAuthor,
            url: ValidUrl,
            labels: [],
            detectedAt: Now);
        _dbContext.Set<Issue>().Add(detected);
        _dbContext.SaveChanges();
        _dbContext.ChangeTracker.Clear();
        return detected;
    }

    private QueuedIssue SeedQueuedIssue(MonitoredRepositoryId repositoryId, int issueNumber)
    {
        DetectedIssue detected = DetectedIssue.Detect(
            repositoryId,
            issueNumber: issueNumber,
            title: "Issue title",
            body: "Body",
            author: ValidAuthor,
            url: ValidUrl,
            labels: [],
            detectedAt: Now);
        QueuedIssue queued = QueuedIssue.FromDetected(detected);
        _dbContext.Set<Issue>().Add(queued);
        _dbContext.SaveChanges();
        _dbContext.ChangeTracker.Clear();
        return queued;
    }

    private BlockedIssue SeedBlockedIssue(MonitoredRepositoryId repositoryId, int issueNumber)
    {
        DetectedIssue detected = DetectedIssue.Detect(
            repositoryId,
            issueNumber: issueNumber,
            title: "Issue title",
            body: "Body",
            author: ValidAuthor,
            url: ValidUrl,
            labels: [],
            detectedAt: Now);
        BlockedIssue blocked = detected.Block([99]);
        _dbContext.Set<Issue>().Add(blocked);
        _dbContext.SaveChanges();
        _dbContext.ChangeTracker.Clear();
        return blocked;
    }

    private FailedIssue SeedFailedIssue(MonitoredRepositoryId repositoryId, int issueNumber)
    {
        DetectedIssue detected = DetectedIssue.Detect(
            repositoryId,
            issueNumber: issueNumber,
            title: "Issue title",
            body: "Body",
            author: ValidAuthor,
            url: ValidUrl,
            labels: [],
            detectedAt: Now);
        QueuedIssue queued = QueuedIssue.FromDetected(detected);
        InProgressIssue inProgress = queued.Claim(Guid.NewGuid());
        FailedIssue failed = inProgress.MarkFailed(Guid.NewGuid(), "Worker exited with code 1", Now, "generic_failure");
        _dbContext.Set<Issue>().Add(failed);
        _dbContext.SaveChanges();
        _dbContext.ChangeTracker.Clear();
        return failed;
    }

    private ContinuableFailedIssue SeedContinuableFailedIssue(MonitoredRepositoryId repositoryId, int issueNumber)
    {
        DetectedIssue detected = DetectedIssue.Detect(
            repositoryId,
            issueNumber: issueNumber,
            title: "Issue title",
            body: "Body",
            author: ValidAuthor,
            url: ValidUrl,
            labels: [],
            detectedAt: Now);
        QueuedIssue queued = QueuedIssue.FromDetected(detected);
        InProgressIssue inProgress = queued.Claim(Guid.NewGuid());
        ContinuableFailedIssue failed = inProgress.MarkContinuableFailed(
            Guid.NewGuid(),
            "feat/1-fix",
            "Continuable failure",
            "generic_failure",
            Now);
        _dbContext.Set<Issue>().Add(failed);
        _dbContext.SaveChanges();
        _dbContext.ChangeTracker.Clear();
        return failed;
    }

    private ContinuationQueuedIssue SeedContinuationQueuedIssue(MonitoredRepositoryId repositoryId, int issueNumber)
    {
        DetectedIssue detected = DetectedIssue.Detect(
            repositoryId,
            issueNumber: issueNumber,
            title: "Issue title",
            body: "Body",
            author: ValidAuthor,
            url: ValidUrl,
            labels: [],
            detectedAt: Now);
        QueuedIssue queued = QueuedIssue.FromDetected(detected);
        InProgressIssue inProgress = queued.Claim(Guid.NewGuid());
        ContinuableFailedIssue failed = inProgress.MarkContinuableFailed(
            Guid.NewGuid(),
            "feat/1-fix",
            "Failure",
            "generic_failure",
            Now);
        ContinuationQueuedIssue continuationQueued = failed.Retry();
        _dbContext.Set<Issue>().Add(continuationQueued);
        _dbContext.SaveChanges();
        _dbContext.ChangeTracker.Clear();
        return continuationQueued;
    }

    private RevisionQueuedIssue SeedRevisionQueuedIssue(MonitoredRepositoryId repositoryId, int issueNumber)
    {
        DetectedIssue detected = DetectedIssue.Detect(
            repositoryId,
            issueNumber: issueNumber,
            title: "Issue title",
            body: "Body",
            author: ValidAuthor,
            url: ValidUrl,
            labels: [],
            detectedAt: Now);
        QueuedIssue queued = QueuedIssue.FromDetected(detected);
        InProgressIssue inProgress = queued.Claim(Guid.NewGuid());
        ReviewIssue review = inProgress.MarkInReview(
            inProgress.WorkerRunId,
            "feat/1-fix",
            "https://github.com/owner/repo/pull/10",
            Now);
        RevisionQueuedIssue revisionQueued = review.Revise([new ReviewComment("Please fix")]);
        _dbContext.Set<Issue>().Add(revisionQueued);
        _dbContext.SaveChanges();
        _dbContext.ChangeTracker.Clear();
        return revisionQueued;
    }

    private RevisionFailedIssue SeedRevisionFailedIssue(MonitoredRepositoryId repositoryId, int issueNumber)
    {
        DetectedIssue detected = DetectedIssue.Detect(
            repositoryId,
            issueNumber: issueNumber,
            title: "Issue title",
            body: "Body",
            author: ValidAuthor,
            url: ValidUrl,
            labels: [],
            detectedAt: Now);
        QueuedIssue queued = QueuedIssue.FromDetected(detected);
        InProgressIssue inProgress = queued.Claim(Guid.NewGuid());
        ReviewIssue review = inProgress.MarkInReview(
            inProgress.WorkerRunId,
            "feat/1-fix",
            "https://github.com/owner/repo/pull/10",
            Now);
        RevisionQueuedIssue revisionQueued = review.Revise([new ReviewComment("Please fix")]);
        RevisionInProgressIssue revisionInProgress = revisionQueued.Claim(Guid.NewGuid());
        RevisionFailedIssue revisionFailed = revisionInProgress.MarkFailed(
            Guid.NewGuid(),
            "Revision failed",
            "generic_failure",
            Now);
        _dbContext.Set<Issue>().Add(revisionFailed);
        _dbContext.SaveChanges();
        _dbContext.ChangeTracker.Clear();
        return revisionFailed;
    }

    private InProgressIssue SeedInProgressIssue(MonitoredRepositoryId repositoryId, int issueNumber)
    {
        DetectedIssue detected = DetectedIssue.Detect(
            repositoryId,
            issueNumber: issueNumber,
            title: "Issue title",
            body: "Body",
            author: ValidAuthor,
            url: ValidUrl,
            labels: [],
            detectedAt: Now);
        QueuedIssue queued = QueuedIssue.FromDetected(detected);
        InProgressIssue inProgress = queued.Claim(Guid.NewGuid());
        _dbContext.Set<Issue>().Add(inProgress);
        _dbContext.SaveChanges();
        _dbContext.ChangeTracker.Clear();
        return inProgress;
    }

    private RevisionInProgressIssue SeedRevisionInProgressIssue(MonitoredRepositoryId repositoryId, int issueNumber)
    {
        DetectedIssue detected = DetectedIssue.Detect(
            repositoryId,
            issueNumber: issueNumber,
            title: "Issue title",
            body: "Body",
            author: ValidAuthor,
            url: ValidUrl,
            labels: [],
            detectedAt: Now);
        QueuedIssue queued = QueuedIssue.FromDetected(detected);
        InProgressIssue inProgress = queued.Claim(Guid.NewGuid());
        ReviewIssue review = inProgress.MarkInReview(
            inProgress.WorkerRunId,
            "feat/1-fix",
            "https://github.com/owner/repo/pull/10",
            Now);
        RevisionQueuedIssue revisionQueued = review.Revise([new ReviewComment("Please fix")]);
        RevisionInProgressIssue revisionInProgress = revisionQueued.Claim(Guid.NewGuid());
        _dbContext.Set<Issue>().Add(revisionInProgress);
        _dbContext.SaveChanges();
        _dbContext.ChangeTracker.Clear();
        return revisionInProgress;
    }

    private ReviewIssue SeedReviewIssue(MonitoredRepositoryId repositoryId, int issueNumber)
    {
        DetectedIssue detected = DetectedIssue.Detect(
            repositoryId,
            issueNumber: issueNumber,
            title: "Issue title",
            body: "Body",
            author: ValidAuthor,
            url: ValidUrl,
            labels: [],
            detectedAt: Now);
        QueuedIssue queued = QueuedIssue.FromDetected(detected);
        InProgressIssue inProgress = queued.Claim(Guid.NewGuid());
        ReviewIssue review = inProgress.MarkInReview(
            inProgress.WorkerRunId,
            "feat/1-fix",
            "https://github.com/owner/repo/pull/10",
            Now);
        _dbContext.Set<Issue>().Add(review);
        _dbContext.SaveChanges();
        _dbContext.ChangeTracker.Clear();
        return review;
    }

    private CompletedIssue SeedCompletedIssue(MonitoredRepositoryId repositoryId, int issueNumber)
    {
        DetectedIssue detected = DetectedIssue.Detect(
            repositoryId,
            issueNumber: issueNumber,
            title: "Issue title",
            body: "Body",
            author: ValidAuthor,
            url: ValidUrl,
            labels: [],
            detectedAt: Now);
        QueuedIssue queued = QueuedIssue.FromDetected(detected);
        InProgressIssue inProgress = queued.Claim(Guid.NewGuid());
        ReviewIssue review = inProgress.MarkInReview(
            inProgress.WorkerRunId,
            "feat/1-fix",
            "https://github.com/owner/repo/pull/10",
            Now);
        CompletedIssue completed = review.Complete(Now);
        _dbContext.Set<Issue>().Add(completed);
        _dbContext.SaveChanges();
        _dbContext.ChangeTracker.Clear();
        return completed;
    }

    private UnchangedIssue SeedUnchangedIssue(MonitoredRepositoryId repositoryId, int issueNumber)
    {
        DetectedIssue detected = DetectedIssue.Detect(
            repositoryId,
            issueNumber: issueNumber,
            title: "Issue title",
            body: "Body",
            author: ValidAuthor,
            url: ValidUrl,
            labels: [],
            detectedAt: Now);
        QueuedIssue queued = QueuedIssue.FromDetected(detected);
        InProgressIssue inProgress = queued.Claim(Guid.NewGuid());
        UnchangedIssue unchanged = inProgress.MarkUnchanged(Guid.NewGuid());
        _dbContext.Set<Issue>().Add(unchanged);
        _dbContext.SaveChanges();
        _dbContext.ChangeTracker.Clear();
        return unchanged;
    }

    // Resting states — should be deleted

    [Fact]
    public async Task WhenDetectedIssue_DeletesRecord()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        DetectedIssue detected = SeedDetectedIssue(repositoryId, issueNumber: 1);
        ProviderIssueUntracked @event = new(repositoryId, 1);

        // Act
        await _sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dbContext.ChangeTracker.Clear();
        Issue? issue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.Id == detected.Id,
                TestContext.Current.CancellationToken);
        issue.ShouldBeNull();
    }

    [Fact]
    public async Task WhenQueuedIssue_DeletesRecord()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        QueuedIssue queued = SeedQueuedIssue(repositoryId, issueNumber: 1);
        ProviderIssueUntracked @event = new(repositoryId, 1);

        // Act
        await _sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dbContext.ChangeTracker.Clear();
        Issue? issue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.Id == queued.Id,
                TestContext.Current.CancellationToken);
        issue.ShouldBeNull();
    }

    [Fact]
    public async Task WhenBlockedIssue_DeletesRecord()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        BlockedIssue blocked = SeedBlockedIssue(repositoryId, issueNumber: 1);
        ProviderIssueUntracked @event = new(repositoryId, 1);

        // Act
        await _sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dbContext.ChangeTracker.Clear();
        Issue? issue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.Id == blocked.Id,
                TestContext.Current.CancellationToken);
        issue.ShouldBeNull();
    }

    [Fact]
    public async Task WhenFailedIssue_DeletesRecord()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        FailedIssue failed = SeedFailedIssue(repositoryId, issueNumber: 1);
        ProviderIssueUntracked @event = new(repositoryId, 1);

        // Act
        await _sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dbContext.ChangeTracker.Clear();
        Issue? issue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.Id == failed.Id,
                TestContext.Current.CancellationToken);
        issue.ShouldBeNull();
    }

    [Fact]
    public async Task WhenContinuableFailedIssue_DeletesRecord()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        ContinuableFailedIssue continuableFailed = SeedContinuableFailedIssue(repositoryId, issueNumber: 1);
        ProviderIssueUntracked @event = new(repositoryId, 1);

        // Act
        await _sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dbContext.ChangeTracker.Clear();
        Issue? issue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.Id == continuableFailed.Id,
                TestContext.Current.CancellationToken);
        issue.ShouldBeNull();
    }

    [Fact]
    public async Task WhenContinuationQueuedIssue_DeletesRecord()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        ContinuationQueuedIssue continuationQueued = SeedContinuationQueuedIssue(repositoryId, issueNumber: 1);
        ProviderIssueUntracked @event = new(repositoryId, 1);

        // Act
        await _sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dbContext.ChangeTracker.Clear();
        Issue? issue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.Id == continuationQueued.Id,
                TestContext.Current.CancellationToken);
        issue.ShouldBeNull();
    }

    [Fact]
    public async Task WhenRevisionQueuedIssue_DeletesRecord()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        RevisionQueuedIssue revisionQueued = SeedRevisionQueuedIssue(repositoryId, issueNumber: 1);
        ProviderIssueUntracked @event = new(repositoryId, 1);

        // Act
        await _sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dbContext.ChangeTracker.Clear();
        Issue? issue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.Id == revisionQueued.Id,
                TestContext.Current.CancellationToken);
        issue.ShouldBeNull();
    }

    [Fact]
    public async Task WhenRevisionFailedIssue_DeletesRecord()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        RevisionFailedIssue revisionFailed = SeedRevisionFailedIssue(repositoryId, issueNumber: 1);
        ProviderIssueUntracked @event = new(repositoryId, 1);

        // Act
        await _sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dbContext.ChangeTracker.Clear();
        Issue? issue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.Id == revisionFailed.Id,
                TestContext.Current.CancellationToken);
        issue.ShouldBeNull();
    }

    // Preserved/active states — should NOT be deleted

    [Fact]
    public async Task WhenInProgressIssue_PreservesRecord()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        InProgressIssue inProgress = SeedInProgressIssue(repositoryId, issueNumber: 1);
        ProviderIssueUntracked @event = new(repositoryId, 1);

        // Act
        await _sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dbContext.ChangeTracker.Clear();
        Issue? issue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.Id == inProgress.Id,
                TestContext.Current.CancellationToken);
        issue.ShouldBeOfType<InProgressIssue>();
    }

    [Fact]
    public async Task WhenRevisionInProgressIssue_PreservesRecord()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        RevisionInProgressIssue revisionInProgress = SeedRevisionInProgressIssue(repositoryId, issueNumber: 1);
        ProviderIssueUntracked @event = new(repositoryId, 1);

        // Act
        await _sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dbContext.ChangeTracker.Clear();
        Issue? issue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.Id == revisionInProgress.Id,
                TestContext.Current.CancellationToken);
        issue.ShouldBeOfType<RevisionInProgressIssue>();
    }

    [Fact]
    public async Task WhenReviewIssue_PreservesRecord()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        ReviewIssue review = SeedReviewIssue(repositoryId, issueNumber: 1);
        ProviderIssueUntracked @event = new(repositoryId, 1);

        // Act
        await _sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dbContext.ChangeTracker.Clear();
        Issue? issue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.Id == review.Id,
                TestContext.Current.CancellationToken);
        issue.ShouldBeOfType<ReviewIssue>();
    }

    [Fact]
    public async Task WhenCompletedIssue_PreservesRecord()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        CompletedIssue completed = SeedCompletedIssue(repositoryId, issueNumber: 1);
        ProviderIssueUntracked @event = new(repositoryId, 1);

        // Act
        await _sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dbContext.ChangeTracker.Clear();
        Issue? issue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.Id == completed.Id,
                TestContext.Current.CancellationToken);
        issue.ShouldBeOfType<CompletedIssue>();
    }

    [Fact]
    public async Task WhenUnchangedIssue_DeletesRecord()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        UnchangedIssue unchanged = SeedUnchangedIssue(repositoryId, issueNumber: 1);
        ProviderIssueUntracked @event = new(repositoryId, 1);

        // Act
        await _sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dbContext.ChangeTracker.Clear();
        Issue? issue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.Id == unchanged.Id,
                TestContext.Current.CancellationToken);
        issue.ShouldBeNull();
    }

    [Fact]
    public async Task WhenIssueNotFound_DoesNotThrow()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        ProviderIssueUntracked @event = new(repositoryId, IssueNumber: 999);

        // Act
        Task act = _sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        await Should.NotThrowAsync(act);
    }
}
