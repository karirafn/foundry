using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Workers.Contracts;
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

namespace Foundry.UnitTests.Modules.Issues.Features.ProviderReactions.ProviderIssueClosedHandlerTests;

public sealed class HandleAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly CapturingDomainEventDispatcher _dispatcher;
    private readonly IIntegrationEventHandler<ProviderIssueClosed> _sut;

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
        _sut = new ProviderIssueClosedHandler(
            _dbContext,
            _dispatcher,
            NullLogger<ProviderIssueClosedHandler>.Instance);
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

    private ChecksPendingIssue SeedChecksPendingIssue(MonitoredRepositoryId repositoryId, int issueNumber = 5)
    {
        ChecksPendingIssue checksPending = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(issueNumber)
            .ChecksPending();
        _dbContext.Set<Issue>().Add(checksPending);
        _dbContext.SaveChanges();
        _dbContext.ChangeTracker.Clear();
        return checksPending;
    }

    [Fact]
    public async Task WhenRebasePendingIssue_TransitionsToCompletedIssue()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        RebasePendingIssue rebasePending = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(7)
            .RebasePending();
        _dbContext.Set<Issue>().Add(rebasePending);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        ProviderIssueClosed @event = new(RepositoryId: repositoryId, IssueNumber: 7);

        // Act
        await _sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dbContext.ChangeTracker.Clear();
        Issue? issue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == repositoryId,
                TestContext.Current.CancellationToken);
        issue.ShouldBeOfType<CompletedIssue>();
    }

    [Fact]
    public async Task WhenReviewIssue_TransitionsToCompletedIssue()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedReviewIssue(repositoryId, issueNumber: 1);

        ProviderIssueClosed @event = new(
            RepositoryId: repositoryId,
            IssueNumber: 1);

        // Act
        await _sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dbContext.ChangeTracker.Clear();
        Issue? issue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == repositoryId,
                TestContext.Current.CancellationToken);
        issue.ShouldBeOfType<CompletedIssue>();
    }

    [Fact]
    public async Task WhenChecksPendingIssue_TransitionsToCompletedIssue()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedChecksPendingIssue(repositoryId, issueNumber: 5);

        ProviderIssueClosed @event = new(
            RepositoryId: repositoryId,
            IssueNumber: 5);

        // Act
        await _sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dbContext.ChangeTracker.Clear();
        Issue? issue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == repositoryId,
                TestContext.Current.CancellationToken);
        issue.ShouldBeOfType<CompletedIssue>();
    }

    [Fact]
    public async Task WhenChecksPendingIssueIsClosed_DispatchesIssueCompletedEvent()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedChecksPendingIssue(repositoryId, issueNumber: 6);

        ProviderIssueClosed @event = new(
            RepositoryId: repositoryId,
            IssueNumber: 6);

        // Act
        await _sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dispatcher.DispatchedEvents
            .OfType<IssueCompleted>()
            .ShouldHaveSingleItem();
    }

    [Fact]
    public async Task WhenUnhandledState_SilentlyIgnores()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedQueuedIssue(repositoryId, issueNumber: 2);

        ProviderIssueClosed @event = new(
            RepositoryId: repositoryId,
            IssueNumber: 2);

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

    public static TheoryData<string> UnpublishedRunStates => ["publish_blocked", "publish_blocked_revision", "publish_failed", "publish_failed_revision"];

    private Issue SeedUnpublishedRunIssue(string state, MonitoredRepositoryId repositoryId, int issueNumber)
    {
        IssueBuilder builder = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(issueNumber)
            .WithBranchName("feat/8-test")
            .WithPullRequestUrl("https://github.com/owner/repo/pull/3");
        Issue issue = state switch
        {
            "publish_blocked" => builder.PublishBlocked(),
            "publish_blocked_revision" => builder.PublishBlockedFromRevision(),
            "publish_failed" => builder.PublishFailed(),
            "publish_failed_revision" => builder.PublishFailedFromRevision(),
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
        };
        _dbContext.Set<Issue>().Add(issue);
        _dbContext.SaveChanges();
        _dbContext.ChangeTracker.Clear();
        return issue;
    }

    [Theory]
    [MemberData(nameof(UnpublishedRunStates))]
    public async Task WhenIssueHoldsAnUnpublishedRun_CompletesKeepingItsBranchAndPullRequest(string state)
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedUnpublishedRunIssue(state, repositoryId, issueNumber: 8);
        string expectedPullRequestUrl = state.EndsWith("_revision", StringComparison.Ordinal)
            ? "https://github.com/owner/repo/pull/3"
            : string.Empty;

        // Act
        await _sut.HandleAsync(new ProviderIssueClosed(repositoryId, 8), CancellationToken.None);

        // Assert
        _dbContext.ChangeTracker.Clear();
        Issue? issue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(i => i.MonitoredRepositoryId == repositoryId, TestContext.Current.CancellationToken);
        CompletedIssue completed = issue.ShouldBeOfType<CompletedIssue>();
        completed.ShouldSatisfyAllConditions(
            () => completed.BranchName.ShouldBe("feat/8-test"),
            () => completed.PullRequestUrl.ShouldBe(expectedPullRequestUrl));
    }

    [Theory]
    [MemberData(nameof(UnpublishedRunStates))]
    public async Task WhenIssueHoldsAnUnpublishedRun_DispatchesIssueClosedWithUnpublishedRunForItsRun(string state)
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        Issue seeded = SeedUnpublishedRunIssue(state, repositoryId, issueNumber: 9);
        WorkerRunId runId = seeded switch
        {
            PublishBlockedIssue blocked => blocked.WorkerRunId,
            PublishFailedIssue failed => failed.WorkerRunId,
            _ => throw new InvalidOperationException(seeded.GetType().Name),
        };

        // Act
        await _sut.HandleAsync(new ProviderIssueClosed(repositoryId, 9), CancellationToken.None);

        // Assert
        _dispatcher.ShouldSatisfyAllConditions(
            () => _dispatcher.DispatchedEvents.OfType<IssueCompleted>().ShouldHaveSingleItem(),
            () => _dispatcher.DispatchedEvents.OfType<IssueClosedWithUnpublishedRun>().ShouldHaveSingleItem()
                .WorkerRunId.ShouldBe(runId));
    }

    [Fact]
    public async Task WhenReviewIssueIsClosed_DispatchesIssueCompletedEvent()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedReviewIssue(repositoryId, issueNumber: 3);

        ProviderIssueClosed @event = new(
            RepositoryId: repositoryId,
            IssueNumber: 3);

        // Act
        await _sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dispatcher.DispatchedEvents
            .OfType<IssueCompleted>()
            .ShouldHaveSingleItem();
    }
}
