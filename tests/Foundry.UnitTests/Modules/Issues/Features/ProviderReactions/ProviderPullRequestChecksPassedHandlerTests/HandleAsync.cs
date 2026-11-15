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

namespace Foundry.UnitTests.Modules.Issues.Features.ProviderReactions.ProviderPullRequestChecksPassedHandlerTests;

public sealed class HandleAsync : IAsyncDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 10, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly CapturingDomainEventDispatcher _dispatcher;
    private readonly IIntegrationEventHandler<ProviderPullRequestChecksPassed> _sut;

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
        _sut = new ProviderPullRequestChecksPassedHandler(
            _dbContext,
            _dispatcher,
            new FakeTimeProvider(Now),
            NullLogger<ProviderPullRequestChecksPassedHandler>.Instance);
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private ReviewIssue SeedReviewIssue(MonitoredRepositoryId repositoryId, ChecksOutcome checksOutcome)
    {
        ReviewIssue review = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(1)
            .WithBranchName("feat/1-checks-rerun")
            .WithPullRequestUrl("https://gitlab.com/group/project/-/merge_requests/10")
            .WithRebaseOutcome(new RebaseOutcome.Rebased())
            .ChecksPendingAtCap()
            .ToReview(checksOutcome, "0a1b2c3d", Now.AddHours(-1));
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
    public async Task WhenReviewIssueChecksAreNotPassed_TransitionsToChecksPendingForAChecksRerun()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        ReviewIssue review = SeedReviewIssue(repositoryId, new ChecksOutcome.StillFailing([]));

        // Act
        await _sut.HandleAsync(new ProviderPullRequestChecksPassed(repositoryId, 1), CancellationToken.None);

        // Assert
        ChecksPendingIssue checksPending = (await LoadAsync(repositoryId)).ShouldBeOfType<ChecksPendingIssue>();
        checksPending.ShouldSatisfyAllConditions(
            () => checksPending.Reason.ShouldBe(new ChecksPendingReason.ChecksRerun()),
            () => checksPending.ChecksFixAttempts.ShouldBe(ChecksPendingIssue.ChecksMaxFixPushes),
            () => checksPending.EnteredChecksPendingAt.ShouldBe(Now),
            () => checksPending.PullRequestUrl.ShouldBe(review.PullRequestUrl));
    }

    [Fact]
    public async Task WhenReviewIssueChecksAreNotPassed_DispatchesIssueChecksPending()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedReviewIssue(repositoryId, new ChecksOutcome.Unfinished());

        // Act
        await _sut.HandleAsync(new ProviderPullRequestChecksPassed(repositoryId, 1), CancellationToken.None);

        // Assert
        _dispatcher.DispatchedEvents.OfType<IssueChecksPending>().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task WhenReviewIssueChecksArePassed_LeavesItUnchanged()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedReviewIssue(repositoryId, new ChecksOutcome.Passed());

        // Act
        await _sut.HandleAsync(new ProviderPullRequestChecksPassed(repositoryId, 1), CancellationToken.None);

        // Assert
        (await LoadAsync(repositoryId)).ShouldBeOfType<ReviewIssue>();
        _dispatcher.DispatchedEvents.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenIssueIsNotInReview_LeavesItUnchanged()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        ChecksPendingIssue checksPending = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(1)
            .ChecksPending();
        _dbContext.Set<Issue>().Add(checksPending);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        await _sut.HandleAsync(new ProviderPullRequestChecksPassed(repositoryId, 1), CancellationToken.None);

        // Assert
        (await LoadAsync(repositoryId)).ShouldBeOfType<ChecksPendingIssue>().Reason.ShouldBe(new ChecksPendingReason.AfterRun());
        _dispatcher.DispatchedEvents.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenIssueNotFound_DoesNotThrow()
    {
        // Act
        Task act = _sut.HandleAsync(
            new ProviderPullRequestChecksPassed(MonitoredRepositoryId.New(), 999),
            CancellationToken.None);

        // Assert
        await Should.NotThrowAsync(act);
    }
}
