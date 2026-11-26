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

namespace Foundry.UnitTests.Modules.Issues.Features.ProviderReactions.ChecksVerdictResolvedHandlerTests;

/// <summary>Routing of a checks verdict when the repository opted in to Merge When Checks Pass (ADR 0105).</summary>
public sealed class AutoMerge : IAsyncDisposable
{
    private const string VerifiedHead = "9e8d7c6b5a4f3e2d1c0b9a8f7e6d5c4b3a291807";

    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly CapturingDomainEventDispatcher _dispatcher;
    private readonly IIntegrationEventHandler<ChecksVerdictResolved> _sut;

    public AutoMerge()
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
            TimeProvider.System,
            NullLogger<ChecksVerdictResolvedHandler>.Instance);
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private void SeedChecksPending(MonitoredRepositoryId repositoryId, bool atFixCap = false)
    {
        IssueBuilder builder = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(1)
            .WithBranchName("feat/1-test-issue")
            .WithPullRequestUrl("https://github.com/owner/repo/pull/10");
        ChecksPendingIssue checksPending = atFixCap ? builder.ChecksPendingAtCap() : builder.ChecksPending();
        _dbContext.Set<Issue>().Add(checksPending);
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
    public async Task WhenPassedAndAutoMerge_TransitionsToMergePendingPinnedToTheVerifiedHead()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedChecksPending(repositoryId);

        // Act
        await _sut.HandleAsync(
            new ChecksVerdictResolved(repositoryId, 1, "passed", [], VerifiedHead, AutoMerge: true),
            TestContext.Current.CancellationToken);

        // Assert
        MergePendingIssue mergePending = (await LoadAsync(repositoryId)).ShouldBeOfType<MergePendingIssue>();
        mergePending.ShouldSatisfyAllConditions(
            () => mergePending.HeadSha.ShouldBe(VerifiedHead),
            () => mergePending.ChecksOutcome.ShouldBeOfType<ChecksOutcome.Passed>(),
            () => mergePending.BranchName.ShouldBe("feat/1-test-issue"),
            () => mergePending.PullRequestUrl.ShouldBe("https://github.com/owner/repo/pull/10"));
    }

    [Fact]
    public async Task WhenPassedAndAutoMerge_RaisesIssueMergePending()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedChecksPending(repositoryId);

        // Act
        await _sut.HandleAsync(
            new ChecksVerdictResolved(repositoryId, 1, "passed", [], VerifiedHead, AutoMerge: true),
            TestContext.Current.CancellationToken);

        // Assert
        IssueMergePending raised = _dispatcher.DispatchedEvents.OfType<IssueMergePending>().ShouldHaveSingleItem();
        raised.IssueNumber.ShouldBe(1);
        _dispatcher.DispatchedEvents.OfType<IssueInReview>().ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenPassedWithoutAutoMerge_TransitionsToRebasePendingBeforeReady()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedChecksPending(repositoryId);

        // Act
        await _sut.HandleAsync(
            new ChecksVerdictResolved(repositoryId, 1, "passed", [], VerifiedHead, AutoMerge: false),
            TestContext.Current.CancellationToken);

        // Assert
        RebasePendingIssue rebasePending = (await LoadAsync(repositoryId)).ShouldBeOfType<RebasePendingIssue>();
        rebasePending.ShouldSatisfyAllConditions(
            () => rebasePending.Reason.ShouldBeOfType<RebasePendingReason.BeforeReady>(),
            () => rebasePending.ChecksOutcome.ShouldBeOfType<ChecksOutcome.Passed>(),
            () => _dispatcher.DispatchedEvents.OfType<IssueRebasePending>().ShouldHaveSingleItem(),
            () => _dispatcher.DispatchedEvents.OfType<IssueMergePending>().ShouldBeEmpty());
    }

    [Fact]
    public async Task WhenPassedAndAutoMergeButNoHead_TransitionsToReview()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedChecksPending(repositoryId);

        // Act
        await _sut.HandleAsync(
            new ChecksVerdictResolved(repositoryId, 1, "passed", [], HeadSha: null, AutoMerge: true),
            TestContext.Current.CancellationToken);

        // Assert
        (await LoadAsync(repositoryId)).ShouldBeOfType<ReviewIssue>();
    }

    [Fact]
    public async Task WhenNoneDetectedAndAutoMerge_TransitionsToRebasePendingBeforeReady()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedChecksPending(repositoryId);

        // Act
        await _sut.HandleAsync(
            new ChecksVerdictResolved(repositoryId, 1, "none_detected", [], VerifiedHead, AutoMerge: true),
            TestContext.Current.CancellationToken);

        // Assert
        RebasePendingIssue rebasePending = (await LoadAsync(repositoryId)).ShouldBeOfType<RebasePendingIssue>();
        rebasePending.ShouldSatisfyAllConditions(
            () => rebasePending.Reason.ShouldBeOfType<RebasePendingReason.BeforeReady>(),
            () => rebasePending.ChecksOutcome.ShouldBeOfType<ChecksOutcome.NoneDetected>());
    }

    [Theory]
    [InlineData("unfinished", typeof(ChecksOutcome.Unfinished))]
    [InlineData("unreadable", typeof(ChecksOutcome.Unreadable))]
    public async Task WhenNotPassedAndAutoMerge_TransitionsToReview(string outcome, Type expectedOutcome)
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedChecksPending(repositoryId);

        // Act
        await _sut.HandleAsync(
            new ChecksVerdictResolved(repositoryId, 1, outcome, [], VerifiedHead, AutoMerge: true),
            TestContext.Current.CancellationToken);

        // Assert
        ReviewIssue review = (await LoadAsync(repositoryId)).ShouldBeOfType<ReviewIssue>();
        review.ChecksOutcome.GetType().ShouldBe(expectedOutcome);
    }

    [Fact]
    public async Task WhenStillFailingUnderCapAndAutoMerge_QueuesChecksFix()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedChecksPending(repositoryId);
        FailingCheck failing = new("ci/build", "FAILURE", "https://github.com/owner/repo/actions/runs/1");

        // Act
        await _sut.HandleAsync(
            new ChecksVerdictResolved(repositoryId, 1, "still_failing", [failing], VerifiedHead, AutoMerge: true),
            TestContext.Current.CancellationToken);

        // Assert
        RevisionQueuedIssue revision = (await LoadAsync(repositoryId)).ShouldBeOfType<RevisionQueuedIssue>();
        revision.Trigger.ShouldBeOfType<RevisionTrigger.FailedChecks>();
    }

    [Fact]
    public async Task WhenStillFailingAtCapAndAutoMerge_TransitionsToReview()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedChecksPending(repositoryId, atFixCap: true);
        FailingCheck failing = new("ci/build", "FAILURE", "https://github.com/owner/repo/actions/runs/1");

        // Act
        await _sut.HandleAsync(
            new ChecksVerdictResolved(repositoryId, 1, "still_failing", [failing], VerifiedHead, AutoMerge: true),
            TestContext.Current.CancellationToken);

        // Assert
        ReviewIssue review = (await LoadAsync(repositoryId)).ShouldBeOfType<ReviewIssue>();
        review.ChecksOutcome.ShouldBeOfType<ChecksOutcome.StillFailing>();
    }
}
