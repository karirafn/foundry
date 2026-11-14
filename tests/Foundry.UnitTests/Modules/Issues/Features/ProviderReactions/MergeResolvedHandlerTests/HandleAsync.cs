using Foundry.Modules.Issues.Contracts;
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

namespace Foundry.UnitTests.Modules.Issues.Features.ProviderReactions.MergeResolvedHandlerTests;

public sealed class HandleAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly CapturingDomainEventDispatcher _dispatcher;
    private readonly IIntegrationEventHandler<MergeResolved> _sut;

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
        _sut = new MergeResolvedHandler(_dbContext, _dispatcher, TimeProvider.System, NullLogger<MergeResolvedHandler>.Instance);
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private void SeedMergePending(MonitoredRepositoryId repositoryId)
    {
        MergePendingIssue mergePending = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(1)
            .WithBranchName("feat/1-test-issue")
            .WithPullRequestUrl("https://github.com/owner/repo/pull/10")
            .MergePending();
        _dbContext.Set<Issue>().Add(mergePending);
        _dbContext.SaveChanges();
        _dbContext.ChangeTracker.Clear();
    }

    private async Task<Issue?> LoadAsync(MonitoredRepositoryId repositoryId)
    {
        _dbContext.ChangeTracker.Clear();
        return await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(i => i.MonitoredRepositoryId == repositoryId, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task WhenMerged_CompletesTheIssue()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedMergePending(repositoryId);

        // Act
        await _sut.HandleAsync(new MergeResolved(repositoryId, 1, "merged"), TestContext.Current.CancellationToken);

        // Assert
        CompletedIssue completed = (await LoadAsync(repositoryId)).ShouldBeOfType<CompletedIssue>();
        completed.PullRequestUrl.ShouldBe("https://github.com/owner/repo/pull/10");
        _dispatcher.DispatchedEvents.OfType<IssueCompleted>().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task WhenDisabled_MovesToReviewWithDisabledOutcome()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedMergePending(repositoryId);

        // Act
        await _sut.HandleAsync(new MergeResolved(repositoryId, 1, "disabled"), TestContext.Current.CancellationToken);

        // Assert
        ReviewIssue review = (await LoadAsync(repositoryId)).ShouldBeOfType<ReviewIssue>();
        review.MergeOutcome.ShouldBe(new MergeOutcome.Disabled());
        _dispatcher.DispatchedEvents.OfType<IssueInReview>().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task WhenFeedback_QueuesAReviewFeedbackRevisionThatHoldsBackTheMerge()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedMergePending(repositoryId);
        ReviewComment comment = new("Please rename this.", "src/a.cs", 3);
        DateTimeOffset newest = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);

        // Act
        await _sut.HandleAsync(
            new MergeResolved(repositoryId, 1, "feedback", Comments: [comment], OmittedCommentCount: 1, NewestCommentAt: newest),
            TestContext.Current.CancellationToken);

        // Assert
        RevisionQueuedIssue revision = (await LoadAsync(repositoryId)).ShouldBeOfType<RevisionQueuedIssue>();
        RevisionTrigger.ReviewFeedback trigger = revision.Trigger.ShouldBeOfType<RevisionTrigger.ReviewFeedback>();
        revision.ShouldSatisfyAllConditions(
            () => revision.HasPersonFeedback.ShouldBeTrue(),
            () => revision.PullRequestUrl.ShouldBe("https://github.com/owner/repo/pull/10"),
            () => trigger.Comments.ShouldHaveSingleItem().ShouldBe(comment),
            () => trigger.OmittedCommentCount.ShouldBe(1),
            () => trigger.NewestConsumedCommentAt.ShouldBe(newest));
        _dispatcher.DispatchedEvents.OfType<IssueRevisionQueued>().ShouldHaveSingleItem();
    }

    [Theory]
    [InlineData(MergeSkipReasons.NotMergeable)]
    [InlineData(MergeSkipReasons.Unauthorized)]
    [InlineData(MergeSkipReasons.HeadMoved)]
    [InlineData(MergeSkipReasons.ProviderError)]
    public async Task WhenSkipped_MovesToReviewWithSkippedReason(string reason)
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedMergePending(repositoryId);

        // Act
        await _sut.HandleAsync(new MergeResolved(repositoryId, 1, "skipped", reason), TestContext.Current.CancellationToken);

        // Assert
        ReviewIssue review = (await LoadAsync(repositoryId)).ShouldBeOfType<ReviewIssue>();
        review.MergeOutcome.ShouldBe(new MergeOutcome.Skipped(reason));
    }

    [Fact]
    public async Task WhenOutcomeUnknown_MovesToReviewAsSkipped()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedMergePending(repositoryId);

        // Act
        await _sut.HandleAsync(new MergeResolved(repositoryId, 1, "squashed"), TestContext.Current.CancellationToken);

        // Assert
        ReviewIssue review = (await LoadAsync(repositoryId)).ShouldBeOfType<ReviewIssue>();
        review.MergeOutcome.ShouldBeOfType<MergeOutcome.Skipped>();
    }

    [Fact]
    public async Task WhenIssueNoLongerMergePending_Ignores()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        ReviewIssue review = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(1)
            .Review();
        _dbContext.Set<Issue>().Add(review);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        await _sut.HandleAsync(new MergeResolved(repositoryId, 1, "merged"), TestContext.Current.CancellationToken);

        // Assert
        (await LoadAsync(repositoryId)).ShouldBeOfType<ReviewIssue>().MergeOutcome.ShouldBeNull();
        _dispatcher.DispatchedEvents.ShouldBeEmpty();
    }
}
