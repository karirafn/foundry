using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Features;
using Foundry.Modules.Issues.Features.Claiming;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Workers.Contracts;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Features.IssuesModuleTests;

/// <summary>The summary serves the issue's whole lifecycle path in wire tokens.</summary>
public sealed class GetIssueSummaryAsync_Lifecycle : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly StubMergeQueueQueries _mergeQueue = new();
    private readonly StubWorkerRunQueries _workerRunQueries = new();
    private readonly StubRepositoryLifecycleSettingsQueries _settings = new();
    private readonly IIssueQueries _sut;

    public GetIssueSummaryAsync_Lifecycle()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new FoundryDbContext(options);
        _dbContext.Database.EnsureCreated();
        _sut = new IssueQueries(
            _dbContext,
            new NullRepositorySlugQueries(),
            new NullRepositoryEligibilityQuery(),
            _workerRunQueries,
            new InFlightWorkerCountQuery(_dbContext, new NullWorkerRunQueries()),
            new NullGlobalSettingsQueries(),
            _mergeQueue,
            _settings);
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private async Task PersistAsync(params Issue[] issues)
    {
        _dbContext.Set<Issue>().AddRange(issues);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static LifecycleStepInfo Current(IssueSummary? summary) =>
        summary.ShouldNotBeNull().Lifecycle.ShouldNotBeNull().Single(s => s.Status == "current");

    [Fact]
    public async Task WhenSummarised_ServesEveryStepInPathOrderWithItsActorAndStatus()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        _settings.Settings[repositoryId] = new RepositoryLifecycleSettings(
            RebaseAfterRun: false,
            ReviewPullRequests: true,
            MergeWhenChecksPass: true);
        ChecksPendingIssue checksPending = new IssueBuilder().WithMonitoredRepositoryId(repositoryId).ChecksPending();
        await PersistAsync(checksPending);

        // Act
        IssueSummary? summary = await _sut.GetIssueSummaryAsync(checksPending.Id, TestContext.Current.CancellationToken);

        // Assert
        summary.ShouldNotBeNull().Lifecycle.ShouldBe(
        [
            new LifecycleStepInfo("detected", "code", "done", null),
            new LifecycleStepInfo("triage_queued", "code", "done", null),
            new LifecycleStepInfo("triage", "claude", "done", null),
            new LifecycleStepInfo("queued", "code", "done", null),
            new LifecycleStepInfo("implement", "claude", "done", null),
            new LifecycleStepInfo("publish", "code", "done", null),
            new LifecycleStepInfo("rebase", "code", "off", null),
            new LifecycleStepInfo("checks", "code", "current", "running"),
            new LifecycleStepInfo("review_queued", "code", "upcoming", null),
            new LifecycleStepInfo("review", "claude", "upcoming", null),
            new LifecycleStepInfo("your_review", "person", "skipped", null),
            new LifecycleStepInfo("merge", "code", "upcoming", null),
            new LifecycleStepInfo("done", "code", "upcoming", null),
        ]);
    }

    [Fact]
    public async Task WhenTheRunIsStarting_TheCurrentStepIsPreparing()
    {
        // Arrange
        InProgressIssue inProgress = new IssueBuilder().InProgress();
        await PersistAsync(inProgress);
        _workerRunQueries.SetRunPhase(inProgress.Id.Value, WorkerRunPhase.Starting);

        // Act
        IssueSummary? summary = await _sut.GetIssueSummaryAsync(inProgress.Id, TestContext.Current.CancellationToken);

        // Assert
        Current(summary).ShouldBe(new LifecycleStepInfo("implement", "claude", "current", "preparing"));
    }

    [Theory]
    [InlineData(WorkerRunPhase.Publishing, "running")]
    [InlineData(WorkerRunPhase.Retrying, "retrying")]
    public async Task WhenTheRunPublishes_TheCurrentStepIsPublish(WorkerRunPhase runPhase, string expectedPhase)
    {
        // Arrange
        RevisionInProgressIssue revision = new IssueBuilder().RevisionInProgress();
        await PersistAsync(revision);
        _workerRunQueries.SetRunPhase(revision.Id.Value, runPhase);

        // Act
        IssueSummary? summary = await _sut.GetIssueSummaryAsync(revision.Id, TestContext.Current.CancellationToken);

        // Assert
        Current(summary).ShouldBe(new LifecycleStepInfo("publish", "code", "current", expectedPhase));
    }

    [Fact]
    public async Task WhenMergePendingWaitsInTheCatchUpQueue_TheCurrentStepIsMergeWaiting()
    {
        // Arrange
        MergePendingIssue mergePending = new IssueBuilder().MergePending();
        await PersistAsync(mergePending);
        _mergeQueue.Waiting.Add((mergePending.MonitoredRepositoryId, mergePending.IssueNumber));

        // Act
        IssueSummary? summary = await _sut.GetIssueSummaryAsync(mergePending.Id, TestContext.Current.CancellationToken);

        // Assert
        Current(summary).ShouldBe(new LifecycleStepInfo("merge", "code", "current", "waiting"));
    }

    [Fact]
    public async Task WhenTheRepositoryHasNoSettingsRow_ThePathReadsTheDefaultSettings()
    {
        // Arrange
        DetectedIssue detected = new IssueBuilder().Detected();
        await PersistAsync(detected);

        // Act
        IssueSummary? summary = await _sut.GetIssueSummaryAsync(detected.Id, TestContext.Current.CancellationToken);

        // Assert
        summary.ShouldNotBeNull().Lifecycle.ShouldNotBeNull()
            .Where(s => s.Status is "off" or "skipped")
            .Select(s => s.Step)
            .ShouldBe(["merge"]);
    }

    [Fact]
    public async Task WhenTheActiveListIsSummarised_ReadsTheSettingsOnceForEveryRepositoryInTheBatch()
    {
        // Arrange
        MonitoredRepositoryId first = MonitoredRepositoryId.New();
        MonitoredRepositoryId second = MonitoredRepositoryId.New();
        await PersistAsync(
            new IssueBuilder().WithMonitoredRepositoryId(first).WithIssueNumber(1).InProgress(),
            new IssueBuilder().WithMonitoredRepositoryId(first).WithIssueNumber(2).ChecksPending(),
            new IssueBuilder().WithMonitoredRepositoryId(second).WithIssueNumber(3).Review());

        // Act
        await _sut.GetActiveIssueSummariesAsync(null, null, TestContext.Current.CancellationToken);

        // Assert
        _settings.Reads.ShouldHaveSingleItem().ShouldBe([first, second], ignoreOrder: true);
    }

    [Fact]
    public async Task WhenTheIssueListIsSummarised_EverySummaryCarriesItsLifecycle()
    {
        // Arrange
        await PersistAsync(new IssueBuilder().WithIssueNumber(1).Detected(), new IssueBuilder().WithIssueNumber(2).Completed());

        // Act
        IReadOnlyList<IssueSummary> summaries = await _sut.GetIssueSummariesAsync(null, TestContext.Current.CancellationToken);

        // Assert
        summaries.Select(s => Current(s).Step).ShouldBe(["detected", "done"], ignoreOrder: true);
    }
}
