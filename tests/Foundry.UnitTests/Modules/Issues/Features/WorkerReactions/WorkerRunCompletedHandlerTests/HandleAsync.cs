using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Issues.Features;
using Foundry.Modules.Issues.Features.WorkerReactions;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Features.WorkerReactions.WorkerRunCompletedHandlerTests;

public sealed class HandleAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly CapturingDomainEventDispatcher _dispatcher;
    private readonly IIntegrationEventHandler<WorkerRunCompleted> _sut;

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
        _dispatcher = new CapturingDomainEventDispatcher();
        _sut = new WorkerRunCompletedHandler(
            _dbContext,
            _dispatcher,
            NullLogger<WorkerRunCompletedHandler>.Instance);
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private InProgressIssue SeedInProgressIssue(MonitoredRepositoryId repositoryId)
    {
        DetectedIssue detected = DetectedIssue.Detect(
            repositoryId,
            issueNumber: 1,
            title: "Issue 1",
            body: "Body",
            author: ValidAuthor,
            url: ValidUrl,
            labels: [],
            detectedAt: DateTimeOffset.UtcNow);
        QueuedIssue queued = QueuedIssue.FromDetected(detected);
        Guid workerRunId = Guid.NewGuid();
        InProgressIssue inProgress = queued.Claim(workerRunId);
        _dbContext.Set<Issue>().Add(inProgress);
        _dbContext.SaveChanges();
        _dbContext.ChangeTracker.Clear();
        return inProgress;
    }

    [Fact]
    public async Task WhenInProgressIssueWithOpenState_TransitionsToReviewIssue()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        InProgressIssue inProgress = SeedInProgressIssue(repositoryId);

        WorkerRunCompleted @event = new(
            WorkerRunId: inProgress.WorkerRunId,
            IssueId: inProgress.Id.Value,
            BranchName: "feat/issue-1-fix",
            PullRequestUrl: "https://github.com/owner/repo/pull/10",
            MergeState: WorkerRunMergeState.Open);

        // Act
        await _sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dbContext.ChangeTracker.Clear();
        Issue? issue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == repositoryId,
                TestContext.Current.CancellationToken);
        ReviewIssue review = issue.ShouldBeOfType<ReviewIssue>();
        review.ShouldSatisfyAllConditions(
            () => review.WorkerRunId.ShouldBe(inProgress.WorkerRunId),
            () => review.BranchName.ShouldBe("feat/issue-1-fix"),
            () => review.PullRequestUrl.ShouldBe("https://github.com/owner/repo/pull/10"));
    }

    [Fact]
    public async Task WhenInProgressIssueWithNoneState_TransitionsToUnchangedIssue()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        InProgressIssue inProgress = SeedInProgressIssue(repositoryId);

        WorkerRunCompleted @event = new(
            WorkerRunId: inProgress.WorkerRunId,
            IssueId: inProgress.Id.Value,
            BranchName: null,
            PullRequestUrl: null,
            MergeState: WorkerRunMergeState.None);

        // Act
        await _sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dbContext.ChangeTracker.Clear();
        Issue? issue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == repositoryId,
                TestContext.Current.CancellationToken);
        UnchangedIssue unchanged = issue.ShouldBeOfType<UnchangedIssue>();
        unchanged.WorkerRunId.ShouldBe(inProgress.WorkerRunId);
    }

    [Fact]
    public async Task WhenInProgressIssueWithBranchButNoneState_TransitionsToUnchangedIssue()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        InProgressIssue inProgress = SeedInProgressIssue(repositoryId);

        WorkerRunCompleted @event = new(
            WorkerRunId: inProgress.WorkerRunId,
            IssueId: inProgress.Id.Value,
            BranchName: "feat/issue-1-fix",
            PullRequestUrl: null,
            MergeState: WorkerRunMergeState.None);

        // Act
        await _sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dbContext.ChangeTracker.Clear();
        Issue? issue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == repositoryId,
                TestContext.Current.CancellationToken);
        UnchangedIssue unchanged = issue.ShouldBeOfType<UnchangedIssue>();
        unchanged.WorkerRunId.ShouldBe(inProgress.WorkerRunId);
    }

    [Fact]
    public async Task WhenInProgressIssueWithMergedState_TransitionsToCompletedIssue()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        InProgressIssue inProgress = SeedInProgressIssue(repositoryId);

        WorkerRunCompleted @event = new(
            WorkerRunId: inProgress.WorkerRunId,
            IssueId: inProgress.Id.Value,
            BranchName: "feat/issue-1-fix",
            PullRequestUrl: "https://github.com/owner/repo/pull/10",
            MergeState: WorkerRunMergeState.Merged);

        // Act
        await _sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dbContext.ChangeTracker.Clear();
        Issue? issue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == repositoryId,
                TestContext.Current.CancellationToken);
        CompletedIssue completed = issue.ShouldBeOfType<CompletedIssue>();
        completed.ShouldSatisfyAllConditions(
            () => completed.BranchName.ShouldBe("feat/issue-1-fix"),
            () => completed.PullRequestUrl.ShouldBe("https://github.com/owner/repo/pull/10"),
            () => completed.CompletedAt.ShouldBeGreaterThan(DateTimeOffset.MinValue));
    }

    [Fact]
    public async Task WhenInProgressIssueWithMergedState_DispatchesIssueCompletedEvent()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        InProgressIssue inProgress = SeedInProgressIssue(repositoryId);

        WorkerRunCompleted @event = new(
            WorkerRunId: inProgress.WorkerRunId,
            IssueId: inProgress.Id.Value,
            BranchName: "feat/issue-1-fix",
            PullRequestUrl: "https://github.com/owner/repo/pull/10",
            MergeState: WorkerRunMergeState.Merged);

        // Act
        await _sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dispatcher.DispatchedEvents
            .OfType<IssueCompleted>()
            .ShouldHaveSingleItem();
    }

    private QueuedIssue SeedQueuedIssue(MonitoredRepositoryId repositoryId)
    {
        DetectedIssue detected = DetectedIssue.Detect(
            repositoryId,
            issueNumber: 2,
            title: "Issue 2",
            body: "Body",
            author: ValidAuthor,
            url: ValidUrl,
            labels: [],
            detectedAt: DateTimeOffset.UtcNow);
        QueuedIssue queued = QueuedIssue.FromDetected(detected);
        _dbContext.Set<Issue>().Add(queued);
        _dbContext.SaveChanges();
        _dbContext.ChangeTracker.Clear();
        return queued;
    }

    private RevisionInProgressIssue SeedRevisionInProgressIssue(MonitoredRepositoryId repositoryId)
    {
        DetectedIssue detected = DetectedIssue.Detect(
            repositoryId,
            issueNumber: 3,
            title: "Issue 3",
            body: "Body",
            author: ValidAuthor,
            url: ValidUrl,
            labels: [],
            detectedAt: DateTimeOffset.UtcNow);
        QueuedIssue queued = QueuedIssue.FromDetected(detected);
        InProgressIssue inProgress = queued.Claim(Guid.NewGuid());
        ReviewIssue review = inProgress.MarkInReview(
            Guid.NewGuid(),
            "feat/issue-3",
            "https://github.com/owner/repo/pull/3",
            DateTimeOffset.UtcNow);
        IReadOnlyList<ReviewComment> comments = [new ReviewComment("Please fix this.")];
        RevisionQueuedIssue revisionQueued = review.Revise(comments);
        RevisionInProgressIssue revisionInProgress = revisionQueued.Claim(Guid.NewGuid());
        _dbContext.Set<Issue>().Add(revisionInProgress);
        _dbContext.SaveChanges();
        _dbContext.ChangeTracker.Clear();
        return revisionInProgress;
    }

    [Fact]
    public async Task WhenRevisionInProgressWithPrUrl_TransitionsToReviewIssue()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        RevisionInProgressIssue revisionInProgress = SeedRevisionInProgressIssue(repositoryId);

        WorkerRunCompleted @event = new(
            WorkerRunId: revisionInProgress.WorkerRunId,
            IssueId: revisionInProgress.Id.Value,
            BranchName: revisionInProgress.BranchName,
            PullRequestUrl: revisionInProgress.PullRequestUrl,
            MergeState: WorkerRunMergeState.Open);

        // Act
        await _sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dbContext.ChangeTracker.Clear();
        Issue? issue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == repositoryId,
                TestContext.Current.CancellationToken);
        ReviewIssue review = issue.ShouldBeOfType<ReviewIssue>();
        review.ShouldSatisfyAllConditions(
            () => review.BranchName.ShouldBe(revisionInProgress.BranchName),
            () => review.PullRequestUrl.ShouldBe(revisionInProgress.PullRequestUrl),
            () => review.FeedbackCutoffAt.ShouldBeGreaterThan(DateTimeOffset.MinValue));
    }

    [Fact]
    public async Task WhenRevisionInProgressWithoutPrUrl_TransitionsToReviewIssue()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        RevisionInProgressIssue revisionInProgress = SeedRevisionInProgressIssue(repositoryId);

        WorkerRunCompleted @event = new(
            WorkerRunId: revisionInProgress.WorkerRunId,
            IssueId: revisionInProgress.Id.Value,
            BranchName: null,
            PullRequestUrl: null,
            MergeState: WorkerRunMergeState.None);

        // Act
        await _sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dbContext.ChangeTracker.Clear();
        Issue? issue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == repositoryId,
                TestContext.Current.CancellationToken);
        ReviewIssue review = issue.ShouldBeOfType<ReviewIssue>();
        review.ShouldSatisfyAllConditions(
            () => review.BranchName.ShouldBe(revisionInProgress.BranchName),
            () => review.PullRequestUrl.ShouldBe(revisionInProgress.PullRequestUrl),
            () => review.FeedbackCutoffAt.ShouldBeGreaterThan(DateTimeOffset.MinValue));
    }

    [Fact]
    public async Task WhenInProgressIssueWithMergedStateAndNullBranchName_DoesNotTransition()
    {
        // Arrange — a Merged event with null BranchName must not be null-suppressed into MarkCompleted
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        InProgressIssue inProgress = SeedInProgressIssue(repositoryId);

        WorkerRunCompleted @event = new(
            WorkerRunId: inProgress.WorkerRunId,
            IssueId: inProgress.Id.Value,
            BranchName: null,
            PullRequestUrl: "https://github.com/owner/repo/pull/10",
            MergeState: WorkerRunMergeState.Merged);

        // Act
        await _sut.HandleAsync(@event, CancellationToken.None);

        // Assert — issue remains InProgress; no transition was attempted
        _dbContext.ChangeTracker.Clear();
        Issue? issue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == repositoryId,
                TestContext.Current.CancellationToken);
        issue.ShouldBeOfType<InProgressIssue>();
    }

    [Fact]
    public async Task WhenInProgressIssueWithMergedStateAndNullPrUrl_DoesNotTransition()
    {
        // Arrange — a Merged event with null PullRequestUrl must not be null-suppressed into MarkCompleted
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        InProgressIssue inProgress = SeedInProgressIssue(repositoryId);

        WorkerRunCompleted @event = new(
            WorkerRunId: inProgress.WorkerRunId,
            IssueId: inProgress.Id.Value,
            BranchName: "feat/issue-1-fix",
            PullRequestUrl: null,
            MergeState: WorkerRunMergeState.Merged);

        // Act
        await _sut.HandleAsync(@event, CancellationToken.None);

        // Assert — issue remains InProgress; no transition was attempted
        _dbContext.ChangeTracker.Clear();
        Issue? issue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == repositoryId,
                TestContext.Current.CancellationToken);
        issue.ShouldBeOfType<InProgressIssue>();
    }

    [Fact]
    public async Task WhenInProgressIssueWithOpenStateAndNullBranchName_DoesNotTransition()
    {
        // Arrange — an Open event with null BranchName must not be null-suppressed into MarkInReview
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        InProgressIssue inProgress = SeedInProgressIssue(repositoryId);

        WorkerRunCompleted @event = new(
            WorkerRunId: inProgress.WorkerRunId,
            IssueId: inProgress.Id.Value,
            BranchName: null,
            PullRequestUrl: "https://github.com/owner/repo/pull/10",
            MergeState: WorkerRunMergeState.Open);

        // Act
        await _sut.HandleAsync(@event, CancellationToken.None);

        // Assert — issue remains InProgress; no transition was attempted
        _dbContext.ChangeTracker.Clear();
        Issue? issue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == repositoryId,
                TestContext.Current.CancellationToken);
        issue.ShouldBeOfType<InProgressIssue>();
    }

    [Fact]
    public async Task WhenInProgressIssueWithOpenStateAndNullPrUrl_DoesNotTransition()
    {
        // Arrange — an Open event with null PullRequestUrl must not be null-suppressed into MarkInReview
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        InProgressIssue inProgress = SeedInProgressIssue(repositoryId);

        WorkerRunCompleted @event = new(
            WorkerRunId: inProgress.WorkerRunId,
            IssueId: inProgress.Id.Value,
            BranchName: "feat/issue-1-fix",
            PullRequestUrl: null,
            MergeState: WorkerRunMergeState.Open);

        // Act
        await _sut.HandleAsync(@event, CancellationToken.None);

        // Assert — issue remains InProgress; no transition was attempted
        _dbContext.ChangeTracker.Clear();
        Issue? issue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == repositoryId,
                TestContext.Current.CancellationToken);
        issue.ShouldBeOfType<InProgressIssue>();
    }

    [Fact]
    public async Task WhenIssueNotInProgress_SilentlyIgnores()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        QueuedIssue queued = SeedQueuedIssue(repositoryId);

        WorkerRunCompleted @event = new(
            WorkerRunId: Guid.NewGuid(),
            IssueId: queued.Id.Value,
            BranchName: "feat/something",
            PullRequestUrl: "https://github.com/owner/repo/pull/5",
            MergeState: WorkerRunMergeState.Open);

        // Act
        Task act = _sut.HandleAsync(@event, CancellationToken.None);

        // Assert — does not throw; issue remains in original state
        await Should.NotThrowAsync(act);
        _dbContext.ChangeTracker.Clear();
        Issue? issue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == repositoryId,
                TestContext.Current.CancellationToken);
        issue.ShouldBeOfType<QueuedIssue>();
    }

    [Fact]
    public async Task WhenInProgressIssueWithOpenState_DispatchesIssueInReviewEvent()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        InProgressIssue inProgress = SeedInProgressIssue(repositoryId);

        WorkerRunCompleted @event = new(
            WorkerRunId: inProgress.WorkerRunId,
            IssueId: inProgress.Id.Value,
            BranchName: "feat/issue-1-fix",
            PullRequestUrl: "https://github.com/owner/repo/pull/10",
            MergeState: WorkerRunMergeState.Open);

        // Act
        await _sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dispatcher.DispatchedEvents
            .OfType<IssueInReview>()
            .ShouldHaveSingleItem();
    }
}
