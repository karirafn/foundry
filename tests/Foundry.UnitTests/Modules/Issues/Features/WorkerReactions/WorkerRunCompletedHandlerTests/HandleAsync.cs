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

    private RevisionInProgressIssue SeedRevisionInProgressIssue(MonitoredRepositoryId repositoryId)
    {
        RevisionInProgressIssue revisionInProgress = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(3)
            .WithBranchName("feat/issue-3")
            .WithPullRequestUrl("https://github.com/owner/repo/pull/3")
            .RevisionInProgress();
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
