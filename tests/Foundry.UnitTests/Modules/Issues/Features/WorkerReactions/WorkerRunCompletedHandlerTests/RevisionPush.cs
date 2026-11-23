using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
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

public sealed class RevisionPush : IAsyncDisposable
{
    private const string BranchName = "feat/issue-5";
    private const string PullRequestUrl = "https://github.com/owner/repo/pull/5";

    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly IIntegrationEventHandler<WorkerRunCompleted> _sut;

    public RevisionPush()
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

    private RevisionInProgressIssue SeedRevisionInProgressIssue(
        MonitoredRepositoryId repositoryId,
        string triggerKind)
    {
        IssueBuilder builder = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(5)
            .WithBranchName(BranchName)
            .WithPullRequestUrl(PullRequestUrl);
        RevisionInProgressIssue revisionInProgress = triggerKind switch
        {
            nameof(RevisionTrigger.FailedChecks) => builder.ChecksPending()
                .FixChecks([new FailingCheck("ci/build", "failure", null)], DateTimeOffset.UtcNow)
                .Claim(WorkerRunId.New(), DateTimeOffset.UtcNow),
            nameof(RevisionTrigger.RebaseConflict) => builder.RebasePending()
                .ResolveConflict(DateTimeOffset.UtcNow)
                .Claim(WorkerRunId.New(), DateTimeOffset.UtcNow),
            nameof(RevisionTrigger.ReviewFeedback) => builder.RevisionInProgress(),
            _ => throw new ArgumentOutOfRangeException(nameof(triggerKind), triggerKind, null),
        };
        _dbContext.Set<Issue>().Add(revisionInProgress);
        _dbContext.SaveChanges();
        _dbContext.ChangeTracker.Clear();
        return revisionInProgress;
    }

    private static WorkerRunCompleted CompletedWithPullRequest(
        RevisionInProgressIssue revisionInProgress,
        RunPublishFacts? publish) =>
        new(
            WorkerRunId: revisionInProgress.WorkerRunId,
            IssueId: revisionInProgress.Id.Value,
            BranchName: BranchName,
            PullRequestUrl: PullRequestUrl,
            MergeState: WorkerRunMergeState.Open,
            Publish: publish);

    private async Task<RebasePendingIssue> LoadRebasePendingAsync(MonitoredRepositoryId repositoryId)
    {
        _dbContext.ChangeTracker.Clear();
        Issue? issue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == repositoryId,
                TestContext.Current.CancellationToken);
        return issue.ShouldBeOfType<RebasePendingIssue>();
    }

    [Fact]
    public async Task WhenFailedChecksRunPushedNothingWithPullRequest_ExhaustsChecksFixBudget()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        RevisionInProgressIssue revisionInProgress =
            SeedRevisionInProgressIssue(repositoryId, nameof(RevisionTrigger.FailedChecks));
        WorkerRunCompleted @event = CompletedWithPullRequest(
            revisionInProgress,
            new RunPublishFacts(null, PullRequestUrl, null, 2));

        // Act
        await _sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        RebasePendingIssue rebasePending = await LoadRebasePendingAsync(repositoryId);
        rebasePending.ChecksFixAttempts.ShouldBe(ChecksPendingIssue.ChecksMaxFixPushes);
    }

    [Fact]
    public async Task WhenFailedChecksRunPushedHead_KeepsChecksFixAttempts()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        RevisionInProgressIssue revisionInProgress =
            SeedRevisionInProgressIssue(repositoryId, nameof(RevisionTrigger.FailedChecks));
        WorkerRunCompleted @event = CompletedWithPullRequest(
            revisionInProgress,
            new RunPublishFacts("abc123", PullRequestUrl, null, 3));

        // Act
        await _sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        RebasePendingIssue rebasePending = await LoadRebasePendingAsync(repositoryId);
        rebasePending.ChecksFixAttempts.ShouldBe(revisionInProgress.ChecksFixAttempts);
        rebasePending.ChecksFixAttempts.ShouldBeLessThan(ChecksPendingIssue.ChecksMaxFixPushes);
    }

    [Fact]
    public async Task WhenFailedChecksEventPredatesPublishFacts_KeepsBranchAndPullRequestRule()
    {
        // Arrange — an event serialized before runs were published carries no Publish facts
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        RevisionInProgressIssue revisionInProgress =
            SeedRevisionInProgressIssue(repositoryId, nameof(RevisionTrigger.FailedChecks));
        WorkerRunCompleted @event = CompletedWithPullRequest(revisionInProgress, publish: null);

        // Act
        await _sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        RebasePendingIssue rebasePending = await LoadRebasePendingAsync(repositoryId);
        rebasePending.ChecksFixAttempts.ShouldBe(revisionInProgress.ChecksFixAttempts);
        rebasePending.ChecksFixAttempts.ShouldBeLessThan(ChecksPendingIssue.ChecksMaxFixPushes);
    }

    [Fact]
    public async Task WhenFailedChecksEventPredatesPublishFactsWithoutPullRequest_ExhaustsChecksFixBudget()
    {
        // Arrange — an event serialized before runs were published carries no Publish facts
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        RevisionInProgressIssue revisionInProgress =
            SeedRevisionInProgressIssue(repositoryId, nameof(RevisionTrigger.FailedChecks));
        WorkerRunCompleted @event = new(
            WorkerRunId: revisionInProgress.WorkerRunId,
            IssueId: revisionInProgress.Id.Value,
            BranchName: null,
            PullRequestUrl: null,
            MergeState: WorkerRunMergeState.None);

        // Act
        await _sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        RebasePendingIssue rebasePending = await LoadRebasePendingAsync(repositoryId);
        rebasePending.ChecksFixAttempts.ShouldBe(ChecksPendingIssue.ChecksMaxFixPushes);
    }

    [Theory]
    [InlineData(nameof(RevisionTrigger.RebaseConflict))]
    [InlineData(nameof(RevisionTrigger.ReviewFeedback))]
    public async Task WhenOtherTriggerRunPushedNothing_KeepsChecksFixAttempts(string triggerKind)
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        RevisionInProgressIssue revisionInProgress = SeedRevisionInProgressIssue(repositoryId, triggerKind);
        WorkerRunCompleted @event = CompletedWithPullRequest(
            revisionInProgress,
            new RunPublishFacts(null, PullRequestUrl, null, 2));

        // Act
        await _sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        RebasePendingIssue rebasePending = await LoadRebasePendingAsync(repositoryId);
        rebasePending.ShouldSatisfyAllConditions(
            () => rebasePending.ChecksFixAttempts.ShouldBe(revisionInProgress.ChecksFixAttempts),
            () => rebasePending.BranchName.ShouldBe(BranchName),
            () => rebasePending.PullRequestUrl.ShouldBe(PullRequestUrl));
    }
}
