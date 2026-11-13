using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.Events;
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
            TimeProvider.System,
            NullLogger<WorkerRunCompletedHandler>.Instance);
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private InProgressIssue SeedInProgressIssue(MonitoredRepositoryId repositoryId)
    {
        IssueBuilder builder = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(1);
        InProgressIssue inProgress = builder.InProgress();
        _dbContext.Set<Issue>().Add(inProgress);
        _dbContext.SaveChanges();
        _dbContext.ChangeTracker.Clear();
        return inProgress;
    }

    [Fact]
    public async Task WhenInProgressIssueWithOpenState_TransitionsToRebasePendingIssue()
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
        RebasePendingIssue rebasePending = issue.ShouldBeOfType<RebasePendingIssue>();
        rebasePending.ShouldSatisfyAllConditions(
            () => rebasePending.WorkerRunId.ShouldBe(inProgress.WorkerRunId),
            () => rebasePending.BranchName.ShouldBe("feat/issue-1-fix"),
            () => rebasePending.PullRequestUrl.ShouldBe("https://github.com/owner/repo/pull/10"));
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

    private FreshQueuedIssue SeedQueuedIssue(MonitoredRepositoryId repositoryId)
    {
        FreshQueuedIssue queued = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(2)
            .FreshQueued();
        _dbContext.Set<Issue>().Add(queued);
        _dbContext.SaveChanges();
        _dbContext.ChangeTracker.Clear();
        return queued;
    }

    private static readonly DateTimeOffset SeededNewestCommentAt =
        new(2024, 6, 1, 10, 0, 0, TimeSpan.Zero);

    private RevisionInProgressIssue SeedRevisionInProgressIssue(MonitoredRepositoryId repositoryId)
    {
        RevisionInProgressIssue revisionInProgress = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(3)
            .WithBranchName("feat/issue-3")
            .WithPullRequestUrl("https://github.com/owner/repo/pull/3")
            .WithNewestCommentAt(SeededNewestCommentAt)
            .RevisionInProgress();
        _dbContext.Set<Issue>().Add(revisionInProgress);
        _dbContext.SaveChanges();
        _dbContext.ChangeTracker.Clear();
        return revisionInProgress;
    }

    [Fact]
    public async Task WhenRevisionInProgressWithPrUrl_TransitionsToRebasePendingIssue()
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
        RebasePendingIssue rebasePending = issue.ShouldBeOfType<RebasePendingIssue>();
        rebasePending.ShouldSatisfyAllConditions(
            () => rebasePending.BranchName.ShouldBe(revisionInProgress.BranchName),
            () => rebasePending.PullRequestUrl.ShouldBe(revisionInProgress.PullRequestUrl),
            () => rebasePending.FeedbackCutoffAt.ShouldBe(SeededNewestCommentAt));
    }

    [Fact]
    public async Task WhenRevisionInProgressWithoutPrUrl_TransitionsToRebasePendingIssue()
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
        RebasePendingIssue rebasePending = issue.ShouldBeOfType<RebasePendingIssue>();
        rebasePending.ShouldSatisfyAllConditions(
            () => rebasePending.BranchName.ShouldBe(revisionInProgress.BranchName),
            () => rebasePending.PullRequestUrl.ShouldBe(revisionInProgress.PullRequestUrl),
            () => rebasePending.FeedbackCutoffAt.ShouldBe(SeededNewestCommentAt));
    }

    [Fact]
    public async Task WhenRevisionInProgressWithPrUrlAndLaterComment_FeedbackCutoffBelowLaterComment()
    {
        // Arrange — comment written AFTER the newest consumed comment (simulates mid-run feedback)
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        RevisionInProgressIssue revisionInProgress = SeedRevisionInProgressIssue(repositoryId);
        DateTimeOffset laterCommentCreatedAt = SeededNewestCommentAt.AddMinutes(5);

        WorkerRunCompleted @event = new(
            WorkerRunId: revisionInProgress.WorkerRunId,
            IssueId: revisionInProgress.Id.Value,
            BranchName: revisionInProgress.BranchName,
            PullRequestUrl: revisionInProgress.PullRequestUrl,
            MergeState: WorkerRunMergeState.Open);

        // Act
        await _sut.HandleAsync(@event, CancellationToken.None);

        // Assert — the cutoff is anchored to NewestConsumedCommentAt, so the later comment
        // falls after the cutoff and the next poll will treat it as actionable feedback
        _dbContext.ChangeTracker.Clear();
        Issue? issue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == repositoryId,
                TestContext.Current.CancellationToken);
        RebasePendingIssue rebasePending = issue.ShouldBeOfType<RebasePendingIssue>();
        rebasePending.FeedbackCutoffAt.ShouldBeLessThan(laterCommentCreatedAt);
    }

    [Fact]
    public async Task WhenRevisionInProgressWithoutPrUrlAndLaterComment_FeedbackCutoffBelowLaterComment()
    {
        // Arrange — comment written AFTER the newest consumed comment (simulates mid-run feedback)
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        RevisionInProgressIssue revisionInProgress = SeedRevisionInProgressIssue(repositoryId);
        DateTimeOffset laterCommentCreatedAt = SeededNewestCommentAt.AddMinutes(5);

        WorkerRunCompleted @event = new(
            WorkerRunId: revisionInProgress.WorkerRunId,
            IssueId: revisionInProgress.Id.Value,
            BranchName: null,
            PullRequestUrl: null,
            MergeState: WorkerRunMergeState.None);

        // Act
        await _sut.HandleAsync(@event, CancellationToken.None);

        // Assert — the cutoff is anchored to NewestConsumedCommentAt, so the later comment
        // falls after the cutoff and the next poll will treat it as actionable feedback
        _dbContext.ChangeTracker.Clear();
        Issue? issue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == repositoryId,
                TestContext.Current.CancellationToken);
        RebasePendingIssue rebasePending = issue.ShouldBeOfType<RebasePendingIssue>();
        rebasePending.FeedbackCutoffAt.ShouldBeLessThan(laterCommentCreatedAt);
    }

    private RevisionInProgressIssue SeedNonFeedbackRevisionInProgressIssue(
        MonitoredRepositoryId repositoryId,
        string triggerKind)
    {
        IssueBuilder builder = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(4)
            .WithBranchName("feat/issue-4")
            .WithPullRequestUrl("https://github.com/owner/repo/pull/4");
        RevisionInProgressIssue revisionInProgress = triggerKind switch
        {
            nameof(RevisionTrigger.FailedChecks) => builder.ChecksPending()
                .FixChecks([new FailingCheck("ci/build", "failure", null)], DateTimeOffset.UtcNow)
                .Claim(WorkerRunId.New(), DateTimeOffset.UtcNow),
            nameof(RevisionTrigger.RebaseConflict) => builder.RebasePending()
                .ResolveConflict(DateTimeOffset.UtcNow)
                .Claim(WorkerRunId.New(), DateTimeOffset.UtcNow),
            _ => throw new ArgumentOutOfRangeException(nameof(triggerKind), triggerKind, null),
        };
        _dbContext.Set<Issue>().Add(revisionInProgress);
        _dbContext.SaveChanges();
        _dbContext.ChangeTracker.Clear();
        return revisionInProgress;
    }

    [Theory]
    [InlineData(nameof(RevisionTrigger.FailedChecks), true)]
    [InlineData(nameof(RevisionTrigger.FailedChecks), false)]
    [InlineData(nameof(RevisionTrigger.RebaseConflict), true)]
    [InlineData(nameof(RevisionTrigger.RebaseConflict), false)]
    public async Task WhenNonFeedbackRevisionCompletes_FeedbackCutoffIsRunStartedAt(string triggerKind, bool withPullRequest)
    {
        // Arrange — a reviewer comments after the run started but before it finished
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        RevisionInProgressIssue revisionInProgress = SeedNonFeedbackRevisionInProgressIssue(repositoryId, triggerKind);
        DateTimeOffset runStartedAt = new(2024, 6, 1, 12, 0, 0, TimeSpan.Zero);
        DateTimeOffset commentDuringRunAt = runStartedAt.AddMinutes(5);

        WorkerRunCompleted @event = new(
            WorkerRunId: revisionInProgress.WorkerRunId,
            IssueId: revisionInProgress.Id.Value,
            BranchName: withPullRequest ? revisionInProgress.BranchName : null,
            PullRequestUrl: withPullRequest ? revisionInProgress.PullRequestUrl : null,
            MergeState: withPullRequest ? WorkerRunMergeState.Open : WorkerRunMergeState.None,
            RunStartedAt: runStartedAt);

        // Act
        await _sut.HandleAsync(@event, CancellationToken.None);

        // Assert — the mid-run comment falls after the cutoff, so the next review pass reads it
        _dbContext.ChangeTracker.Clear();
        Issue? issue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == repositoryId,
                TestContext.Current.CancellationToken);
        RebasePendingIssue rebasePending = issue.ShouldBeOfType<RebasePendingIssue>();
        rebasePending.ShouldSatisfyAllConditions(
            () => rebasePending.FeedbackCutoffAt.ShouldBe(runStartedAt),
            () => rebasePending.FeedbackCutoffAt.ShouldBeLessThan(commentDuringRunAt));
    }

    [Fact]
    public async Task WhenNonFeedbackRevisionCompletesWithoutRunStartedAt_FeedbackCutoffFallsBackToUtcNow()
    {
        // Arrange — an event serialized before RunStartedAt existed
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        RevisionInProgressIssue revisionInProgress =
            SeedNonFeedbackRevisionInProgressIssue(repositoryId, nameof(RevisionTrigger.FailedChecks));
        DateTimeOffset before = DateTimeOffset.UtcNow;

        WorkerRunCompleted @event = new(
            WorkerRunId: revisionInProgress.WorkerRunId,
            IssueId: revisionInProgress.Id.Value,
            BranchName: revisionInProgress.BranchName,
            PullRequestUrl: revisionInProgress.PullRequestUrl,
            MergeState: WorkerRunMergeState.Open,
            RunStartedAt: null);

        // Act
        await _sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        DateTimeOffset after = DateTimeOffset.UtcNow;
        _dbContext.ChangeTracker.Clear();
        Issue? issue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == repositoryId,
                TestContext.Current.CancellationToken);
        issue.ShouldBeOfType<RebasePendingIssue>().FeedbackCutoffAt.ShouldBeInRange(before, after);
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
        // Arrange — an Open event with null BranchName must not be null-suppressed into MarkRebasePending
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
        // Arrange — an Open event with null PullRequestUrl must not be null-suppressed into MarkRebasePending
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
        FreshQueuedIssue queued = SeedQueuedIssue(repositoryId);

        WorkerRunCompleted @event = new(
            WorkerRunId: WorkerRunId.New(),
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
        issue.ShouldBeOfType<FreshQueuedIssue>();
    }

    [Fact]
    public async Task WhenInProgressIssueWithOpenState_DispatchesIssueRebasePendingEvent()
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
            .OfType<IssueRebasePending>()
            .ShouldHaveSingleItem();
    }

    [Fact]
    public async Task WhenInProgressIssueWithOpenStateAndRunStartedAt_SetsFeedbackCutoffToRunStartedAt()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        InProgressIssue inProgress = SeedInProgressIssue(repositoryId);
        DateTimeOffset runStartedAt = new DateTimeOffset(2024, 3, 10, 8, 30, 0, TimeSpan.Zero);

        WorkerRunCompleted @event = new(
            WorkerRunId: inProgress.WorkerRunId,
            IssueId: inProgress.Id.Value,
            BranchName: "feat/issue-1-fix",
            PullRequestUrl: "https://github.com/owner/repo/pull/10",
            MergeState: WorkerRunMergeState.Open,
            RunStartedAt: runStartedAt);

        // Act
        await _sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dbContext.ChangeTracker.Clear();
        Issue? issue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == repositoryId,
                TestContext.Current.CancellationToken);
        RebasePendingIssue rebasePending = issue.ShouldBeOfType<RebasePendingIssue>();
        rebasePending.FeedbackCutoffAt.ShouldBe(runStartedAt);
    }

    [Fact]
    public async Task WhenInProgressIssueWithOpenStateAndNoRunStartedAt_SetsFeedbackCutoffToUtcNow()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        InProgressIssue inProgress = SeedInProgressIssue(repositoryId);
        DateTimeOffset before = DateTimeOffset.UtcNow;

        WorkerRunCompleted @event = new(
            WorkerRunId: inProgress.WorkerRunId,
            IssueId: inProgress.Id.Value,
            BranchName: "feat/issue-1-fix",
            PullRequestUrl: "https://github.com/owner/repo/pull/10",
            MergeState: WorkerRunMergeState.Open,
            RunStartedAt: null);

        // Act
        await _sut.HandleAsync(@event, CancellationToken.None);
        DateTimeOffset after = DateTimeOffset.UtcNow;

        // Assert
        _dbContext.ChangeTracker.Clear();
        Issue? issue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == repositoryId,
                TestContext.Current.CancellationToken);
        RebasePendingIssue rebasePending = issue.ShouldBeOfType<RebasePendingIssue>();
        rebasePending.FeedbackCutoffAt.ShouldBeInRange(before, after);
    }
}
