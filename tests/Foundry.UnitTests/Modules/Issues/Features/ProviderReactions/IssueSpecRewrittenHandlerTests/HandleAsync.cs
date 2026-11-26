using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Issues.Features.ProviderReactions;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Testing;
using Foundry.UnitTests.Modules.Monitoring.Features.Providers.Feedback;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Features.ProviderReactions.IssueSpecRewrittenHandlerTests;

/// <summary>A rewritten spec sends an issue with open work back to triage, holding that work (ADR 0125).</summary>
public sealed class HandleAsync : IAsyncDisposable
{
    private const string Branch = "feat/3-add-retry";
    private const string PullRequest = "https://github.com/owner/repo/pull/9";

    private static readonly DateTimeOffset Now = new(2026, 11, 25, 10, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly CapturingDomainEventDispatcher _domainEvents = new();
    private readonly IssueSpecRewrittenHandler _sut;
    private readonly MonitoredRepositoryId _repositoryId = MonitoredRepositoryId.New();

    public HandleAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new FoundryDbContext(options);
        _dbContext.Database.EnsureCreated();
        _sut = new IssueSpecRewrittenHandler(
            _dbContext,
            _domainEvents,
            new FakeTimeProvider(Now),
            NullLogger<IssueSpecRewrittenHandler>.Instance);
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private IssueBuilder Builder() =>
        new IssueBuilder()
            .WithMonitoredRepositoryId(_repositoryId)
            .WithIssueNumber(3)
            .WithBranchName(Branch)
            .WithPullRequestUrl(PullRequest)
            .WithTransitionedAt(Now.AddHours(-1))
            .WithFailedAt(Now.AddHours(-1));

    private void Seed(Issue issue)
    {
        _dbContext.Set<Issue>().Add(issue);
        _dbContext.SaveChanges();
        _dbContext.ChangeTracker.Clear();
    }

    private async Task<Issue> HandleAsync_()
    {
        await _sut.HandleAsync(new IssueSpecRewritten(_repositoryId, 3, Now.AddMinutes(-3)), TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();
        return await _dbContext.Set<Issue>().SingleAsync(TestContext.Current.CancellationToken);
    }

    private void AssertQueuedForTriageHolding(Issue issue, OpenWork openWork)
    {
        TriageQueuedIssue queued = issue.ShouldBeOfType<TriageQueuedIssue>();
        queued.ShouldSatisfyAllConditions(
            () => queued.OpenWork.ShouldBe(openWork),
            () => queued.EnteredStateAt.ShouldBe(Now),
            () => _domainEvents.DispatchedEvents.OfType<IssueTriageQueued>().ShouldHaveSingleItem());
    }

    [Fact]
    public async Task WhenTheIssueIsRevisionQueued_QueuesTriageHoldingItsBranchAndPullRequest()
    {
        // Arrange
        Seed(Builder().RevisionQueued());

        // Act
        Issue issue = await HandleAsync_();

        // Assert
        AssertQueuedForTriageHolding(issue, OpenWork.Of(Branch, PullRequest));
    }

    [Fact]
    public async Task WhenTheIssueIsRevisionBlocked_QueuesTriageHoldingItsOpenWorkAndBlockers()
    {
        // Arrange
        Seed(Builder().RevisionBlocked([5]));

        // Act
        Issue issue = await HandleAsync_();

        // Assert
        AssertQueuedForTriageHolding(issue, OpenWork.Of(Branch, PullRequest));
        issue.BlockedBy.ShouldBe([5]);
    }

    [Fact]
    public async Task WhenTheIssueIsInReview_QueuesTriageHoldingItsBranchAndPullRequest()
    {
        // Arrange
        Seed(Builder().Review());

        // Act
        Issue issue = await HandleAsync_();

        // Assert
        AssertQueuedForTriageHolding(issue, OpenWork.Of(Branch, PullRequest));
    }

    [Fact]
    public async Task WhenTheIssueIsRevisionFailed_QueuesTriageHoldingItsBranchAndPullRequest()
    {
        // Arrange
        Seed(Builder().RevisionFailed());

        // Act
        Issue issue = await HandleAsync_();

        // Assert
        AssertQueuedForTriageHolding(issue, OpenWork.Of(Branch, PullRequest));
    }

    [Fact]
    public async Task WhenTheIssueIsContinuationQueued_QueuesTriageHoldingItsBranchOnly()
    {
        // Arrange
        Seed(Builder().ContinuationQueued());

        // Act
        Issue issue = await HandleAsync_();

        // Assert
        AssertQueuedForTriageHolding(issue, OpenWork.OnBranch(Branch));
    }

    [Fact]
    public async Task WhenTheIssueIsContinuationBlocked_QueuesTriageHoldingItsBranchOnly()
    {
        // Arrange
        Seed(Builder().ContinuationBlocked([5]));

        // Act
        Issue issue = await HandleAsync_();

        // Assert
        AssertQueuedForTriageHolding(issue, OpenWork.OnBranch(Branch));
    }

    [Fact]
    public async Task WhenTheIssueIsInTheCodeOwnedPipeline_LeavesItAsItIs()
    {
        // Arrange
        Seed(Builder().ChecksPending());

        // Act
        Issue issue = await HandleAsync_();

        // Assert
        issue.ShouldBeOfType<ChecksPendingIssue>();
        _domainEvents.DispatchedEvents.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenARunIsInProgress_LeavesItAsItIs()
    {
        // Arrange
        Seed(Builder().RevisionInProgress());

        // Act
        Issue issue = await HandleAsync_();

        // Assert
        issue.ShouldBeOfType<RevisionInProgressIssue>();
        _domainEvents.DispatchedEvents.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenTheIssueIsNotTracked_DoesNothing()
    {
        // Act
        await _sut.HandleAsync(new IssueSpecRewritten(_repositoryId, 3, Now), TestContext.Current.CancellationToken);

        // Assert
        (await _dbContext.Set<Issue>().AnyAsync(TestContext.Current.CancellationToken)).ShouldBeFalse();
        _domainEvents.DispatchedEvents.ShouldBeEmpty();
    }
}
