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

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Features.ProviderReactions.ChecksVerdictResolvedHandlerTests;

public sealed class HandleAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly CapturingDomainEventDispatcher _dispatcher;
    private readonly IIntegrationEventHandler<ChecksVerdictResolved> _sut;

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
        _sut = new ChecksVerdictResolvedHandler(
            _dbContext,
            _dispatcher,
            NullLogger<ChecksVerdictResolvedHandler>.Instance);
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private ChecksPendingIssue SeedChecksPendingIssue(
        MonitoredRepositoryId repositoryId,
        int issueNumber = 1,
        DateTimeOffset? feedbackCutoffAt = null)
    {
        ChecksPendingIssue checksPending = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(issueNumber)
            .WithBranchName("feat/1-test-issue")
            .WithPullRequestUrl("https://github.com/owner/repo/pull/10")
            .WithFeedbackCutoffAt(feedbackCutoffAt ?? new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero))
            .ChecksPending();
        _dbContext.Set<Issue>().Add(checksPending);
        _dbContext.SaveChanges();
        _dbContext.ChangeTracker.Clear();
        return checksPending;
    }

    private FreshQueuedIssue SeedQueuedIssue(MonitoredRepositoryId repositoryId, int issueNumber = 2)
    {
        FreshQueuedIssue queued = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(issueNumber)
            .FreshQueued();
        _dbContext.Set<Issue>().Add(queued);
        _dbContext.SaveChanges();
        _dbContext.ChangeTracker.Clear();
        return queued;
    }

    private ReviewIssue SeedReviewIssue(MonitoredRepositoryId repositoryId, int issueNumber = 3)
    {
        ReviewIssue review = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(issueNumber)
            .Review();
        _dbContext.Set<Issue>().Add(review);
        _dbContext.SaveChanges();
        _dbContext.ChangeTracker.Clear();
        return review;
    }

    [Fact]
    public async Task WhenChecksPendingAndOutcomePassed_TransitionsToReviewIssue()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedChecksPendingIssue(repositoryId, issueNumber: 1);

        ChecksVerdictResolved @event = new(
            RepositoryId: repositoryId,
            IssueNumber: 1,
            Outcome: "passed",
            FailingChecks: []);

        // Act
        await _sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dbContext.ChangeTracker.Clear();
        Issue? issue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == repositoryId,
                TestContext.Current.CancellationToken);
        ReviewIssue reviewIssue = issue.ShouldBeOfType<ReviewIssue>();
        reviewIssue.ChecksOutcome.ShouldBeOfType<ChecksOutcome.Passed>();
    }

    [Fact]
    public async Task WhenChecksPendingAndOutcomeNoneDetected_TransitionsToReviewIssueWithNoneDetected()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedChecksPendingIssue(repositoryId, issueNumber: 1);

        ChecksVerdictResolved @event = new(
            RepositoryId: repositoryId,
            IssueNumber: 1,
            Outcome: "none_detected",
            FailingChecks: []);

        // Act
        await _sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dbContext.ChangeTracker.Clear();
        Issue? issue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == repositoryId,
                TestContext.Current.CancellationToken);
        ReviewIssue reviewIssue = issue.ShouldBeOfType<ReviewIssue>();
        reviewIssue.ChecksOutcome.ShouldBeOfType<ChecksOutcome.NoneDetected>();
    }

    [Fact]
    public async Task WhenChecksPendingAndOutcomeUnfinished_TransitionsToReviewIssueWithUnfinished()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedChecksPendingIssue(repositoryId, issueNumber: 1);

        ChecksVerdictResolved @event = new(
            RepositoryId: repositoryId,
            IssueNumber: 1,
            Outcome: "unfinished",
            FailingChecks: []);

        // Act
        await _sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dbContext.ChangeTracker.Clear();
        Issue? issue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == repositoryId,
                TestContext.Current.CancellationToken);
        ReviewIssue reviewIssue = issue.ShouldBeOfType<ReviewIssue>();
        reviewIssue.ChecksOutcome.ShouldBeOfType<ChecksOutcome.Unfinished>();
    }

    [Fact]
    public async Task WhenChecksPendingAndOutcomeStillFailing_TransitionsToReviewIssueWithFailingChecks()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedChecksPendingIssue(repositoryId, issueNumber: 1);

        IReadOnlyList<FailingCheck> failingChecks =
        [
            new FailingCheck("ci/build", "failure", "https://github.com/owner/repo/actions/runs/1"),
            new FailingCheck("ci/lint", "failure", null),
        ];

        ChecksVerdictResolved @event = new(
            RepositoryId: repositoryId,
            IssueNumber: 1,
            Outcome: "still_failing",
            FailingChecks: failingChecks);

        // Act
        await _sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dbContext.ChangeTracker.Clear();
        Issue? issue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == repositoryId,
                TestContext.Current.CancellationToken);
        ReviewIssue reviewIssue = issue.ShouldBeOfType<ReviewIssue>();
        ChecksOutcome.StillFailing stillFailing = reviewIssue.ChecksOutcome.ShouldBeOfType<ChecksOutcome.StillFailing>();
        stillFailing.FailingChecks.Count.ShouldBe(2);
        stillFailing.FailingChecks[0].Name.ShouldBe("ci/build");
        stillFailing.FailingChecks[1].Name.ShouldBe("ci/lint");
    }

    [Fact]
    public async Task WhenChecksPendingAndOutcomeUnreadable_TransitionsToReviewIssueWithUnreadable()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedChecksPendingIssue(repositoryId, issueNumber: 1);

        ChecksVerdictResolved @event = new(
            RepositoryId: repositoryId,
            IssueNumber: 1,
            Outcome: "unreadable",
            FailingChecks: []);

        // Act
        await _sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dbContext.ChangeTracker.Clear();
        Issue? issue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == repositoryId,
                TestContext.Current.CancellationToken);
        ReviewIssue reviewIssue = issue.ShouldBeOfType<ReviewIssue>();
        reviewIssue.ChecksOutcome.ShouldBeOfType<ChecksOutcome.Unreadable>();
    }

    [Fact]
    public async Task WhenChecksPendingIssueTransitions_FeedbackCutoffAtIsCarriedForward()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        DateTimeOffset expectedCutoff = new DateTimeOffset(2026, 12, 31, 23, 59, 59, TimeSpan.Zero);
        ChecksPendingIssue seeded = SeedChecksPendingIssue(repositoryId, issueNumber: 1, feedbackCutoffAt: expectedCutoff);

        ChecksVerdictResolved @event = new(
            RepositoryId: repositoryId,
            IssueNumber: 1,
            Outcome: "passed",
            FailingChecks: []);

        // Act
        await _sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dbContext.ChangeTracker.Clear();
        Issue? issue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == repositoryId,
                TestContext.Current.CancellationToken);
        ReviewIssue reviewIssue = issue.ShouldBeOfType<ReviewIssue>();
        reviewIssue.FeedbackCutoffAt.ShouldBe(seeded.FeedbackCutoffAt);
    }

    [Fact]
    public async Task WhenChecksPendingIssueTransitions_DispatchesIssueInReviewEvent()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedChecksPendingIssue(repositoryId, issueNumber: 1);

        ChecksVerdictResolved @event = new(
            RepositoryId: repositoryId,
            IssueNumber: 1,
            Outcome: "passed",
            FailingChecks: []);

        // Act
        await _sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dispatcher.DispatchedEvents
            .OfType<IssueInReview>()
            .ShouldHaveSingleItem();
    }

    [Fact]
    public async Task WhenIssueIsNotChecksPending_SilentlyIgnores()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedQueuedIssue(repositoryId, issueNumber: 2);

        ChecksVerdictResolved @event = new(
            RepositoryId: repositoryId,
            IssueNumber: 2,
            Outcome: "passed",
            FailingChecks: []);

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
    public async Task WhenIssueAlreadyInReviewState_SilentlyIgnores()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedReviewIssue(repositoryId, issueNumber: 3);

        ChecksVerdictResolved @event = new(
            RepositoryId: repositoryId,
            IssueNumber: 3,
            Outcome: "passed",
            FailingChecks: []);

        // Act
        Task act = _sut.HandleAsync(@event, CancellationToken.None);

        // Assert — redelivery is idempotent; issue stays ReviewIssue
        await Should.NotThrowAsync(act);
        _dbContext.ChangeTracker.Clear();
        Issue? issue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == repositoryId,
                TestContext.Current.CancellationToken);
        issue.ShouldBeOfType<ReviewIssue>();
    }

    [Fact]
    public async Task WhenIssueNotFound_SilentlyIgnores()
    {
        // Arrange
        MonitoredRepositoryId unknownRepositoryId = MonitoredRepositoryId.New();

        ChecksVerdictResolved @event = new(
            RepositoryId: unknownRepositoryId,
            IssueNumber: 999,
            Outcome: "passed",
            FailingChecks: []);

        // Act
        Task act = _sut.HandleAsync(@event, CancellationToken.None);

        // Assert — does not throw
        await Should.NotThrowAsync(act);
    }
}
