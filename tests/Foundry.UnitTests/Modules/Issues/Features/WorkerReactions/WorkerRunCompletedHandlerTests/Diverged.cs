using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Features.WorkerReactions;
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

public sealed class Diverged : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly IIntegrationEventHandler<WorkerRunCompleted> _sut;

    public Diverged()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new FoundryDbContext(options);
        _dbContext.Database.EnsureCreated();
        _sut = new WorkerRunCompletedHandler(
            _dbContext,
            new CapturingDomainEventDispatcher(),
            TimeProvider.System,
            NullLogger<WorkerRunCompletedHandler>.Instance);
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private T Seed<T>(T issue)
        where T : Issue
    {
        _dbContext.Set<Issue>().Add(issue);
        _dbContext.SaveChanges();
        _dbContext.ChangeTracker.Clear();
        return issue;
    }

    private async Task<Issue> ReloadAsync(Issue issue)
    {
        _dbContext.ChangeTracker.Clear();
        return await _dbContext.Set<Issue>()
            .SingleAsync(i => i.Id == issue.Id, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task WhenImplementRunDiverged_MovesToBranchDiverged()
    {
        // Arrange
        InProgressIssue inProgress = Seed(new IssueBuilder().InProgress());
        WorkerRunCompleted @event = new(
            WorkerRunId: inProgress.WorkerRunId,
            IssueId: inProgress.Id.Value,
            BranchName: "feat/1-test",
            PullRequestUrl: null,
            MergeState: WorkerRunMergeState.None,
            Publish: new RunPublishFacts(null, null, "feat/1-test-run-abc", 0));

        // Act
        await _sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert
        BranchDivergedIssue diverged = (await ReloadAsync(inProgress)).ShouldBeOfType<BranchDivergedIssue>();
        diverged.ShouldSatisfyAllConditions(
            () => diverged.WorkerRunId.ShouldBe(inProgress.WorkerRunId),
            () => diverged.BranchName.ShouldBe("feat/1-test"),
            () => diverged.SideBranchName.ShouldBe("feat/1-test-run-abc"),
            () => diverged.DivergenceReason.ShouldBe(BranchDivergenceReason.BranchMoved),
            () => diverged.FromRevision.ShouldBeFalse());
    }

    [Fact]
    public async Task WhenImplementRunDivergedWithOpenPullRequest_KeepsThePullRequest()
    {
        // Arrange
        InProgressIssue inProgress = Seed(new IssueBuilder().InProgress());
        WorkerRunCompleted @event = new(
            WorkerRunId: inProgress.WorkerRunId,
            IssueId: inProgress.Id.Value,
            BranchName: "feat/1-test",
            PullRequestUrl: "https://github.com/owner/repo/pull/7",
            MergeState: WorkerRunMergeState.Open,
            Publish: new RunPublishFacts(null, "https://github.com/owner/repo/pull/7", "feat/1-test-run-abc", 2));

        // Act
        await _sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert
        BranchDivergedIssue diverged = (await ReloadAsync(inProgress)).ShouldBeOfType<BranchDivergedIssue>();
        diverged.LinkedPullRequestUrl.ShouldBe("https://github.com/owner/repo/pull/7");
    }

    [Fact]
    public async Task WhenRevisionRunDiverged_MovesToBranchDivergedFromRevision()
    {
        // Arrange
        RevisionInProgressIssue revision = Seed(new IssueBuilder()
            .WithBranchName("feat/2-fix")
            .WithPullRequestUrl("https://github.com/owner/repo/pull/2")
            .RevisionInProgress());
        WorkerRunCompleted @event = new(
            WorkerRunId: revision.WorkerRunId,
            IssueId: revision.Id.Value,
            BranchName: "feat/2-fix",
            PullRequestUrl: "https://github.com/owner/repo/pull/2",
            MergeState: WorkerRunMergeState.Open,
            Publish: new RunPublishFacts(null, "https://github.com/owner/repo/pull/2", "feat/2-fix-run-abc", 3));

        // Act
        await _sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert
        BranchDivergedIssue diverged = (await ReloadAsync(revision)).ShouldBeOfType<BranchDivergedIssue>();
        diverged.ShouldSatisfyAllConditions(
            () => diverged.BranchName.ShouldBe("feat/2-fix"),
            () => diverged.SideBranchName.ShouldBe("feat/2-fix-run-abc"),
            () => diverged.FromRevision.ShouldBeTrue());
    }

    // ADR 0101: the provider refused the lease push of a rebased tip, so the run's work went to a side branch.
    [Fact]
    public async Task WhenImplementRunDivergedOnAForcePushProtectedBranch_KeepsTheReason()
    {
        // Arrange
        InProgressIssue inProgress = Seed(new IssueBuilder().InProgress());
        WorkerRunCompleted @event = new(
            WorkerRunId: inProgress.WorkerRunId,
            IssueId: inProgress.Id.Value,
            BranchName: "feat/1-test",
            PullRequestUrl: null,
            MergeState: WorkerRunMergeState.None,
            Publish: new RunPublishFacts(null, null, "feat/1-test-run-abc", 0, BranchDivergenceReason.ForcePushProtected));

        // Act
        await _sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert
        (await ReloadAsync(inProgress)).ShouldBeOfType<BranchDivergedIssue>()
            .DivergenceReason.ShouldBe(BranchDivergenceReason.ForcePushProtected);
    }

    [Fact]
    public async Task WhenRevisionRunDivergedOnAForcePushProtectedBranch_KeepsTheReason()
    {
        // Arrange
        RevisionInProgressIssue revision = Seed(new IssueBuilder()
            .WithBranchName("feat/2-fix")
            .WithPullRequestUrl("https://github.com/owner/repo/pull/2")
            .RevisionInProgress());
        WorkerRunCompleted @event = new(
            WorkerRunId: revision.WorkerRunId,
            IssueId: revision.Id.Value,
            BranchName: "feat/2-fix",
            PullRequestUrl: "https://github.com/owner/repo/pull/2",
            MergeState: WorkerRunMergeState.Open,
            Publish: new RunPublishFacts(
                null,
                "https://github.com/owner/repo/pull/2",
                "feat/2-fix-run-abc",
                3,
                BranchDivergenceReason.ForcePushProtected));

        // Act
        await _sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert
        (await ReloadAsync(revision)).ShouldBeOfType<BranchDivergedIssue>()
            .DivergenceReason.ShouldBe(BranchDivergenceReason.ForcePushProtected);
    }

    [Fact]
    public async Task WhenPublishedWithoutDivergence_TransitionsAsBefore()
    {
        // Arrange
        InProgressIssue inProgress = Seed(new IssueBuilder().InProgress());
        WorkerRunCompleted @event = new(
            WorkerRunId: inProgress.WorkerRunId,
            IssueId: inProgress.Id.Value,
            BranchName: "feat/1-test",
            PullRequestUrl: "https://github.com/owner/repo/pull/7",
            MergeState: WorkerRunMergeState.Open,
            Publish: new RunPublishFacts("abc123", "https://github.com/owner/repo/pull/7", null, 2));

        // Act
        await _sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert
        (await ReloadAsync(inProgress)).ShouldBeOfType<RebasePendingIssue>();
    }
}
