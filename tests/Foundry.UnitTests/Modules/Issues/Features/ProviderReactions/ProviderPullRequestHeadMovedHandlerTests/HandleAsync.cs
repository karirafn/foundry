using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Issues.Features.ProviderReactions;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Foundry.UnitTests.Modules.Monitoring.Features.Providers.Feedback;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Features.ProviderReactions.ProviderPullRequestHeadMovedHandlerTests;

public sealed class HandleAsync : IAsyncDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 10, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly CapturingDomainEventDispatcher _dispatcher;
    private readonly IIntegrationEventHandler<ProviderPullRequestHeadMoved> _sut;

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
        _sut = new ProviderPullRequestHeadMovedHandler(
            _dbContext,
            _dispatcher,
            new FakeTimeProvider(Now),
            NullLogger<ProviderPullRequestHeadMovedHandler>.Instance);
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private ReviewIssue SeedReviewIssueAfterExhaustedFixes(MonitoredRepositoryId repositoryId, DateTimeOffset feedbackCutoffAt)
    {
        ReviewIssue review = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(1)
            .WithBranchName("feat/1-head-moved")
            .WithPullRequestUrl("https://github.com/owner/repo/pull/10")
            .WithFeedbackCutoffAt(feedbackCutoffAt)
            .WithRebaseOutcome(new RebaseOutcome.Rebased())
            .ChecksPendingAtCap()
            .ToReview(new ChecksOutcome.StillFailing([]), "0a1b2c3d", Now.AddHours(-1));
        _dbContext.Set<Issue>().Add(review);
        _dbContext.SaveChanges();
        _dbContext.ChangeTracker.Clear();
        return review;
    }

    private async Task<Issue?> LoadAsync(MonitoredRepositoryId repositoryId)
    {
        _dbContext.ChangeTracker.Clear();
        return await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == repositoryId,
                TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task WhenReviewIssue_TransitionsToChecksPendingWithFreshFixBudget()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        DateTimeOffset feedbackCutoffAt = new(2026, 11, 12, 8, 0, 0, TimeSpan.Zero);
        ReviewIssue review = SeedReviewIssueAfterExhaustedFixes(repositoryId, feedbackCutoffAt);

        // Act
        await _sut.HandleAsync(new ProviderPullRequestHeadMoved(repositoryId, 1), CancellationToken.None);

        // Assert
        ChecksPendingIssue checksPending = (await LoadAsync(repositoryId)).ShouldBeOfType<ChecksPendingIssue>();
        checksPending.ShouldSatisfyAllConditions(
            () => checksPending.ChecksFixAttempts.ShouldBe(0),
            () => checksPending.EnteredChecksPendingAt.ShouldBe(Now),
            () => checksPending.WorkerRunId.ShouldBe(review.WorkerRunId),
            () => checksPending.BranchName.ShouldBe("feat/1-head-moved"),
            () => checksPending.PullRequestUrl.ShouldBe("https://github.com/owner/repo/pull/10"),
            () => checksPending.FeedbackCutoffAt.ShouldBe(review.FeedbackCutoffAt),
            () => checksPending.RebaseOutcome.ShouldBe(new RebaseOutcome.Rebased()),
            () => checksPending.RebaseConflictAttempts.ShouldBe(review.RebaseConflictAttempts));
    }

    [Fact]
    public async Task WhenReviewIssue_DispatchesIssueChecksPending()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedReviewIssueAfterExhaustedFixes(repositoryId, Now.AddDays(-1));

        // Act
        await _sut.HandleAsync(new ProviderPullRequestHeadMoved(repositoryId, 1), CancellationToken.None);

        // Assert
        _dispatcher.DispatchedEvents.OfType<IssueChecksPending>().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task WhenIssueIsNotInReview_LeavesItUnchanged()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        RevisionQueuedIssue revisionQueued = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(1)
            .RevisionQueued();
        _dbContext.Set<Issue>().Add(revisionQueued);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        await _sut.HandleAsync(new ProviderPullRequestHeadMoved(repositoryId, 1), CancellationToken.None);

        // Assert
        (await LoadAsync(repositoryId)).ShouldBeOfType<RevisionQueuedIssue>();
        _dispatcher.DispatchedEvents.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenIssueNotFound_DoesNotThrow()
    {
        // Act
        Task act = _sut.HandleAsync(
            new ProviderPullRequestHeadMoved(MonitoredRepositoryId.New(), 999),
            CancellationToken.None);

        // Assert
        await Should.NotThrowAsync(act);
    }
}
