using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.Events;
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

namespace Foundry.UnitTests.Modules.Issues.Features.ProviderReactions.PullRequestChangesRequestedHandlerTests;

public sealed class HandleAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly CapturingDomainEventDispatcher _dispatcher;
    private readonly IIntegrationEventHandler<PullRequestChangesRequested> _sut;

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
        _sut = new PullRequestChangesRequestedHandler(
            _dbContext,
            _dispatcher,
            NullLogger<PullRequestChangesRequestedHandler>.Instance);
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private ReviewIssue SeedReviewIssue(MonitoredRepositoryId repositoryId, int issueNumber = 1)
    {
        ReviewIssue review = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(issueNumber)
            .WithBranchName("feat/issue-1-fix")
            .WithPullRequestUrl("https://github.com/owner/repo/pull/10")
            .Review();
        _dbContext.Set<Issue>().Add(review);
        _dbContext.SaveChanges();
        _dbContext.ChangeTracker.Clear();
        return review;
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

    [Fact]
    public async Task WhenReviewIssueExists_TransitionsToRevisionQueuedIssue()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedReviewIssue(repositoryId, issueNumber: 1);

        IReadOnlyList<ReviewComment> comments =
        [
            new ReviewComment("Please fix the null check", "src/Service.cs", Line: 42),
        ];

        PullRequestChangesRequested @event = new(
            RepositoryId: repositoryId,
            IssueNumber: 1,
            Comments: comments);

        // Act
        await _sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dbContext.ChangeTracker.Clear();
        Issue? issue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == repositoryId,
                TestContext.Current.CancellationToken);
        RevisionQueuedIssue revisionQueued = issue.ShouldBeOfType<RevisionQueuedIssue>();
        revisionQueued.ShouldSatisfyAllConditions(
            () => revisionQueued.BranchName.ShouldBe("feat/issue-1-fix"),
            () => revisionQueued.PullRequestUrl.ShouldBe("https://github.com/owner/repo/pull/10"),
            () => revisionQueued.ReviewComments.Count.ShouldBe(1),
            () => revisionQueued.ReviewComments[0].Body.ShouldBe("Please fix the null check"));
    }

    [Fact]
    public async Task WhenIssueNotInReviewState_SilentlyIgnores()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedQueuedIssue(repositoryId, issueNumber: 2);

        PullRequestChangesRequested @event = new(
            RepositoryId: repositoryId,
            IssueNumber: 2,
            Comments: [new ReviewComment("Comment", "file.cs", Line: 1)]);

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
    public async Task WhenIssueNotFound_SilentlyIgnores()
    {
        // Arrange
        MonitoredRepositoryId unknownRepositoryId = MonitoredRepositoryId.New();

        PullRequestChangesRequested @event = new(
            RepositoryId: unknownRepositoryId,
            IssueNumber: 999,
            Comments: [new ReviewComment("Comment", "file.cs", Line: 1)]);

        // Act
        Task act = _sut.HandleAsync(@event, CancellationToken.None);

        // Assert — does not throw
        await Should.NotThrowAsync(act);
    }

    [Fact]
    public async Task WhenReviewIssueGetsChangesRequested_DispatchesIssueRevisionQueuedEvent()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedReviewIssue(repositoryId, issueNumber: 3);

        PullRequestChangesRequested @event = new(
            RepositoryId: repositoryId,
            IssueNumber: 3,
            Comments: [new ReviewComment("Please update this.", "src/Service.cs", Line: 10)]);

        // Act
        await _sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dispatcher.DispatchedEvents
            .OfType<IssueRevisionQueued>()
            .ShouldHaveSingleItem();
    }

    [Fact]
    public async Task WhenEventHasOmittedCommentCount_PropagatesItToRevisionQueuedIssue()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedReviewIssue(repositoryId, issueNumber: 4);

        PullRequestChangesRequested @event = new(
            RepositoryId: repositoryId,
            IssueNumber: 4,
            Comments: [new ReviewComment("Fix this.", "src/A.cs", Line: 5)],
            OmittedCommentCount: 7);

        // Act
        await _sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dbContext.ChangeTracker.Clear();
        Issue? issue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == repositoryId,
                TestContext.Current.CancellationToken);
        RevisionQueuedIssue revisionQueued = issue.ShouldBeOfType<RevisionQueuedIssue>();
        revisionQueued.OmittedCommentCount.ShouldBe(7);
    }

    [Fact]
    public async Task WhenEventHasNewestCommentAt_PropagatesItToRevisionQueuedIssue()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedReviewIssue(repositoryId, issueNumber: 5);

        DateTimeOffset newestCommentAt = new DateTimeOffset(2026, 9, 1, 10, 0, 0, TimeSpan.Zero);
        PullRequestChangesRequested @event = new(
            RepositoryId: repositoryId,
            IssueNumber: 5,
            Comments: [new ReviewComment("Fix this.", "src/A.cs", Line: 5)],
            NewestCommentAt: newestCommentAt);

        // Act
        await _sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dbContext.ChangeTracker.Clear();
        Issue? issue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == repositoryId,
                TestContext.Current.CancellationToken);
        RevisionQueuedIssue revisionQueued = issue.ShouldBeOfType<RevisionQueuedIssue>();
        revisionQueued.NewestConsumedCommentAt.ShouldBe(newestCommentAt);
    }
}
