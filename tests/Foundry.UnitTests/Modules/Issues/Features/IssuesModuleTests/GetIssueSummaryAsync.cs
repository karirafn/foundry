using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Issues.Features;
using Foundry.Modules.Issues.Features.Claiming;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Contracts.Queries;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Features.IssuesModuleTests;

public sealed class GetIssueSummaryAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly StubWorkerRunQueries _workerRunQueries;
    private readonly StubRepositorySlugQueries _slugQueries;
    private readonly IIssueQueries _sut;

    public GetIssueSummaryAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new FoundryDbContext(options);
        _dbContext.Database.EnsureCreated();
        _workerRunQueries = new StubWorkerRunQueries();
        _slugQueries = new StubRepositorySlugQueries();
        _sut = new IssueQueries(
            _dbContext,
            _slugQueries,
            new NullRepositoryEligibilityQuery(),
            _workerRunQueries,
            new InFlightWorkerCountQuery(_dbContext),
            new NullGlobalSettingsQueries());
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private async Task<DetectedIssue> CreateAndPersistDetectedIssueAsync(MonitoredRepositoryId repositoryId)
    {
        DetectedIssue issue = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .Detected();

        _dbContext.Set<Issue>().Add(issue);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        return issue;
    }

    [Fact]
    public async Task WhenIssueDoesNotExist_ReturnsNull()
    {
        // Arrange
        IssueId unknownId = IssueId.New();

        // Act
        IssueSummary? result = await _sut.GetIssueSummaryAsync(unknownId, TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public async Task WhenRepositoryCoveredByGitLabCredential_SummaryCarriesSlugAndProviderType()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        _slugQueries.AddSlug(repositoryId, "group/project", "gitlab");
        DetectedIssue detected = await CreateAndPersistDetectedIssueAsync(repositoryId);

        // Act
        IssueSummary? result = await _sut.GetIssueSummaryAsync(detected.Id, TestContext.Current.CancellationToken);

        // Assert
        IssueSummary summary = result.ShouldNotBeNull();
        summary.ShouldSatisfyAllConditions(
            () => summary.RepositorySlug.ShouldBe("group/project"),
            () => summary.ProviderType.ShouldBe("gitlab"));
    }

    [Fact]
    public async Task WhenRepositoryHasNoDisplayInfo_SummaryHasEmptySlugAndNullProviderType()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        DetectedIssue detected = await CreateAndPersistDetectedIssueAsync(repositoryId);

        // Act
        IssueSummary? result = await _sut.GetIssueSummaryAsync(detected.Id, TestContext.Current.CancellationToken);

        // Assert
        IssueSummary summary = result.ShouldNotBeNull();
        summary.ShouldSatisfyAllConditions(
            () => summary.RepositorySlug.ShouldBe(string.Empty),
            () => summary.ProviderType.ShouldBeNull());
    }

    [Theory]
    [InlineData(WorkerRunPublishPhase.Publishing, "publishing")]
    [InlineData(WorkerRunPublishPhase.Retrying, "retrying")]
    public async Task WhenInProgressRunIsPublishing_SummaryCarriesThePublishPhase(
        WorkerRunPublishPhase phase,
        string expected)
    {
        // Arrange
        InProgressIssue inProgress = new IssueBuilder().InProgress();
        _dbContext.Set<Issue>().Add(inProgress);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _workerRunQueries.SetPublishPhase(inProgress.Id.Value, phase);

        // Act
        IssueSummary? result = await _sut.GetIssueSummaryAsync(inProgress.Id, TestContext.Current.CancellationToken);

        // Assert
        result.ShouldNotBeNull().PublishPhase.ShouldBe(expected);
    }

    [Fact]
    public async Task WhenRevisionRunIsPublishing_SummaryCarriesThePublishPhase()
    {
        // Arrange
        RevisionInProgressIssue revision = new IssueBuilder().RevisionInProgress();
        _dbContext.Set<Issue>().Add(revision);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _workerRunQueries.SetPublishPhase(revision.Id.Value, WorkerRunPublishPhase.Publishing);

        // Act
        IssueSummary? result = await _sut.GetIssueSummaryAsync(revision.Id, TestContext.Current.CancellationToken);

        // Assert
        result.ShouldNotBeNull().PublishPhase.ShouldBe("publishing");
    }

    [Fact]
    public async Task WhenIssueIsNotWorking_SummaryHasNoPublishPhase()
    {
        // Arrange
        PublishFailedIssue publishFailed = new IssueBuilder().PublishFailed();
        _dbContext.Set<Issue>().Add(publishFailed);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _workerRunQueries.SetPublishPhase(publishFailed.Id.Value, WorkerRunPublishPhase.Retrying);

        // Act
        IssueSummary? result = await _sut.GetIssueSummaryAsync(publishFailed.Id, TestContext.Current.CancellationToken);

        // Assert
        IssueSummary summary = result.ShouldNotBeNull();
        summary.ShouldSatisfyAllConditions(
            () => summary.State.ShouldBe("publish_failed"),
            () => summary.PublishPhase.ShouldBeNull());
    }

    [Fact]
    public async Task WhenActiveListHasAPublishingRun_ItsSummaryCarriesThePublishPhase()
    {
        // Arrange
        InProgressIssue inProgress = new IssueBuilder().InProgress();
        _dbContext.Set<Issue>().Add(inProgress);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _workerRunQueries.SetPublishPhase(inProgress.Id.Value, WorkerRunPublishPhase.Retrying);

        // Act
        IReadOnlyList<IssueSummary> result = await _sut.GetActiveIssueSummariesAsync(
            null,
            null,
            TestContext.Current.CancellationToken);

        // Assert
        result.ShouldHaveSingleItem().PublishPhase.ShouldBe("retrying");
    }

    [Theory]
    [InlineData("review", "awaiting_review")]
    [InlineData("rebase_pending", "implement")]
    [InlineData("completed", "done")]
    public async Task WhenInState_SummaryCarriesTheStateStage(string state, string expectedStage)
    {
        // Arrange
        Issue issue = BuildState(new IssueBuilder(), state);
        _dbContext.Set<Issue>().Add(issue);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        IssueSummary? result = await _sut.GetIssueSummaryAsync(issue.Id, TestContext.Current.CancellationToken);

        // Assert
        result.ShouldNotBeNull().Stage.ShouldBe(expectedStage);
    }

    [Fact]
    public async Task WhenDetected_SummaryIsInTriageAwaitingDispatch()
    {
        // Arrange
        DetectedIssue detected = await CreateAndPersistDetectedIssueAsync(MonitoredRepositoryId.New());

        // Act
        IssueSummary? result = await _sut.GetIssueSummaryAsync(detected.Id, TestContext.Current.CancellationToken);

        // Assert
        IssueSummary summary = result.ShouldNotBeNull();
        summary.ShouldSatisfyAllConditions(
            () => summary.Stage.ShouldBe("triage"),
            () => summary.NeedsAction.ShouldBeFalse(),
            () => summary.AwaitsDispatch.ShouldBeTrue(),
            () => summary.CodeStep.ShouldBeNull());
    }

    [Fact]
    public async Task WhenReview_SummaryNeedsAction()
    {
        // Arrange
        ReviewIssue review = new IssueBuilder().Review();
        _dbContext.Set<Issue>().Add(review);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        IssueSummary? result = await _sut.GetIssueSummaryAsync(review.Id, TestContext.Current.CancellationToken);

        // Assert
        IssueSummary summary = result.ShouldNotBeNull();
        summary.ShouldSatisfyAllConditions(
            () => summary.NeedsAction.ShouldBeTrue(),
            () => summary.AwaitsDispatch.ShouldBeFalse());
    }

    [Theory]
    [InlineData("rebase_pending", "rebasing")]
    [InlineData("checks_pending", "checks")]
    public async Task WhenStateRunsACodeStep_SummaryCarriesTheCodeStep(string state, string expectedCodeStep)
    {
        // Arrange
        Issue issue = BuildState(new IssueBuilder(), state);
        _dbContext.Set<Issue>().Add(issue);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        IssueSummary? result = await _sut.GetIssueSummaryAsync(issue.Id, TestContext.Current.CancellationToken);

        // Assert
        result.ShouldNotBeNull().CodeStep.ShouldBe(expectedCodeStep);
    }

    [Theory]
    [InlineData(WorkerRunPublishPhase.Publishing, "publishing")]
    [InlineData(WorkerRunPublishPhase.Retrying, "publish_retrying")]
    public async Task WhenRunIsPublishing_SummaryCodeStepIsThePublishStep(
        WorkerRunPublishPhase phase,
        string expectedCodeStep)
    {
        // Arrange
        InProgressIssue inProgress = new IssueBuilder().InProgress();
        _dbContext.Set<Issue>().Add(inProgress);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _workerRunQueries.SetPublishPhase(inProgress.Id.Value, phase);

        // Act
        IssueSummary? result = await _sut.GetIssueSummaryAsync(inProgress.Id, TestContext.Current.CancellationToken);

        // Assert
        result.ShouldNotBeNull().CodeStep.ShouldBe(expectedCodeStep);
    }

    [Fact]
    public async Task WhenInProgressRunIsNotPublishing_SummaryHasNoCodeStep()
    {
        // Arrange
        InProgressIssue inProgress = new IssueBuilder().InProgress();
        _dbContext.Set<Issue>().Add(inProgress);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        IssueSummary? result = await _sut.GetIssueSummaryAsync(inProgress.Id, TestContext.Current.CancellationToken);

        // Assert
        result.ShouldNotBeNull().CodeStep.ShouldBeNull();
    }

    [Fact]
    public async Task WhenIssueHasNoWorkerRuns_RunStatsIsNull()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        DetectedIssue issue = await CreateAndPersistDetectedIssueAsync(repositoryId);

        // Act
        IssueSummary? result = await _sut.GetIssueSummaryAsync(issue.Id, TestContext.Current.CancellationToken);

        // Assert
        result.ShouldNotBeNull();
        result.RunStats.ShouldBeNull();
    }

    [Fact]
    public async Task WhenIssueHasWorkerRuns_RunStatsIsPopulated()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        DetectedIssue issue = await CreateAndPersistDetectedIssueAsync(repositoryId);

        RunAggregate aggregate = new(
            RunCount: 2,
            DurationMs: 3000L,
            NumTurns: 8,
            TotalCostUsd: 0.30m,
            InputTokens: new TokenTotal(1300L, KnownRunCount: 1, UnknownRunCount: 1),
            OutputTokens: new TokenTotal(500L, KnownRunCount: 2, UnknownRunCount: 0));

        _workerRunQueries.SetAggregate(issue.Id.Value, aggregate);

        // Act
        IssueSummary? result = await _sut.GetIssueSummaryAsync(issue.Id, TestContext.Current.CancellationToken);

        // Assert
        result.ShouldNotBeNull();
        RunStats stats = result.RunStats.ShouldNotBeNull();
        stats.ShouldSatisfyAllConditions(
            () => stats.RunCount.ShouldBe(2),
            () => stats.DurationMs.ShouldBe(3000L),
            () => stats.NumTurns.ShouldBe(8),
            () => stats.TotalCostUsd.ShouldBe(0.30m),
            () => stats.InputTokens.ShouldBe(new TokenTotal(1300L, KnownRunCount: 1, UnknownRunCount: 1)),
            () => stats.OutputTokens.ShouldBe(new TokenTotal(500L, KnownRunCount: 2, UnknownRunCount: 0)));
    }

    [Fact]
    public async Task WhenReviewIssue_ChecksOutcomeIsPopulatedOnSummary()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        DetectedIssue detected = await CreateAndPersistDetectedIssueAsync(repositoryId);
        FreshQueuedIssue queued = detected.Enqueue();
        await _dbContext.TransitionAsync(detected, queued, new NullDomainEventDispatcher(), TestContext.Current.CancellationToken);

        InProgressIssue inProgress = queued.Claim(WorkerRunId.New());
        await _dbContext.TransitionAsync(queued, inProgress, new NullDomainEventDispatcher(), TestContext.Current.CancellationToken);

        ChecksPendingIssue checksPending = inProgress.MarkRebasePending(
            "feat/1-test",
            "https://github.com/owner/repo/pull/1",
            DateTimeOffset.UtcNow.AddDays(1))
            .MarkChecksPending(new RebaseOutcome.UpToDate());
        await _dbContext.TransitionAsync(inProgress, checksPending, new NullDomainEventDispatcher(), TestContext.Current.CancellationToken);

        ReviewIssue review = checksPending.ToReview(new ChecksOutcome.Passed());
        await _dbContext.TransitionAsync(checksPending, review, new NullDomainEventDispatcher(), TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        IssueSummary? result = await _sut.GetIssueSummaryAsync(review.Id, TestContext.Current.CancellationToken);

        // Assert
        IssueSummary summary = result.ShouldNotBeNull();
        summary.State.ShouldBe("review");
        summary.ChecksOutcome.ShouldBe("passed");
    }

    [Fact]
    public async Task WhenChecksPendingIssue_PullRequestUrlIsPopulatedOnSummary()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        DetectedIssue detected = await CreateAndPersistDetectedIssueAsync(repositoryId);
        FreshQueuedIssue queued = detected.Enqueue();
        await _dbContext.TransitionAsync(detected, queued, new NullDomainEventDispatcher(), TestContext.Current.CancellationToken);

        InProgressIssue inProgress = queued.Claim(WorkerRunId.New());
        await _dbContext.TransitionAsync(queued, inProgress, new NullDomainEventDispatcher(), TestContext.Current.CancellationToken);

        const string ExpectedPrUrl = "https://github.com/owner/repo/pull/7";
        ChecksPendingIssue checksPending = inProgress.MarkRebasePending(
            "feat/1-test",
            ExpectedPrUrl,
            DateTimeOffset.UtcNow.AddDays(1))
            .MarkChecksPending(new RebaseOutcome.UpToDate());
        await _dbContext.TransitionAsync(inProgress, checksPending, new NullDomainEventDispatcher(), TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        IssueSummary? result = await _sut.GetIssueSummaryAsync(checksPending.Id, TestContext.Current.CancellationToken);

        // Assert
        IssueSummary summary = result.ShouldNotBeNull();
        summary.State.ShouldBe("checks_pending");
        summary.PullRequestUrl.ShouldBe(ExpectedPrUrl);
    }

    [Fact]
    public async Task WhenNonReviewIssue_ChecksOutcomeIsNullOnSummary()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        DetectedIssue detected = await CreateAndPersistDetectedIssueAsync(repositoryId);

        // Act
        IssueSummary? result = await _sut.GetIssueSummaryAsync(detected.Id, TestContext.Current.CancellationToken);

        // Assert
        IssueSummary summary = result.ShouldNotBeNull();
        summary.State.ShouldBe("detected");
        summary.ChecksOutcome.ShouldBeNull();
    }

    [Theory]
    [InlineData("review")]
    [InlineData("checks_pending")]
    [InlineData("rebase_pending")]
    [InlineData("revision_queued")]
    [InlineData("revision_in_progress")]
    [InlineData("revision_failed")]
    [InlineData("continuable_failed")]
    [InlineData("completed")]
    public async Task WhenStateStoresPullRequestUrl_PullRequestUrlIsPopulatedOnSummary(string state)
    {
        // Arrange
        const string ExpectedPrUrl = "https://github.com/owner/repo/pull/9";
        IssueBuilder builder = new IssueBuilder().WithPullRequestUrl(ExpectedPrUrl);
        Issue issue = BuildState(builder, state);
        _dbContext.Set<Issue>().Add(issue);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        IssueSummary? result = await _sut.GetIssueSummaryAsync(issue.Id, TestContext.Current.CancellationToken);

        // Assert
        IssueSummary summary = result.ShouldNotBeNull();
        summary.State.ShouldBe(state);
        summary.PullRequestUrl.ShouldBe(ExpectedPrUrl);
    }

    [Fact]
    public async Task WhenContinuableFailedStoresEmptyPullRequestUrl_PullRequestUrlIsNullOnSummary()
    {
        // Arrange — failing from in_progress before a PR opened stores string.Empty
        ContinuableFailedIssue issue = new IssueBuilder().ContinuableFailed();
        _dbContext.Set<Issue>().Add(issue);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        IssueSummary? result = await _sut.GetIssueSummaryAsync(issue.Id, TestContext.Current.CancellationToken);

        // Assert
        IssueSummary summary = result.ShouldNotBeNull();
        summary.State.ShouldBe("continuable_failed");
        summary.PullRequestUrl.ShouldBeNull();
    }

    [Fact]
    public async Task WhenCompletedStoresEmptyPullRequestUrl_PullRequestUrlIsNullOnSummary()
    {
        // Arrange
        CompletedIssue issue = new IssueBuilder().WithPullRequestUrl(string.Empty).Completed();
        _dbContext.Set<Issue>().Add(issue);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        IssueSummary? result = await _sut.GetIssueSummaryAsync(issue.Id, TestContext.Current.CancellationToken);

        // Assert
        IssueSummary summary = result.ShouldNotBeNull();
        summary.State.ShouldBe("completed");
        summary.PullRequestUrl.ShouldBeNull();
    }

    [Theory]
    [InlineData("review_feedback", "revision_queued")]
    [InlineData("review_feedback", "revision_in_progress")]
    [InlineData("review_feedback", "revision_failed")]
    [InlineData("failed_checks", "revision_queued")]
    [InlineData("failed_checks", "revision_in_progress")]
    [InlineData("failed_checks", "revision_failed")]
    [InlineData("rebase_conflict", "revision_queued")]
    [InlineData("rebase_conflict", "revision_in_progress")]
    [InlineData("rebase_conflict", "revision_failed")]
    [InlineData("input_provided", "revision_queued")]
    [InlineData("input_provided", "revision_in_progress")]
    [InlineData("input_provided", "revision_failed")]
    public async Task WhenRevisionState_RevisionTriggerKindIsTheTriggerDiscriminator(string triggerKind, string state)
    {
        // Arrange
        Issue issue = BuildRevisionState(new IssueBuilder(), triggerKind, state);
        _dbContext.Set<Issue>().Add(issue);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        IssueSummary? result = await _sut.GetIssueSummaryAsync(issue.Id, TestContext.Current.CancellationToken);

        // Assert
        IssueSummary summary = result.ShouldNotBeNull();
        summary.State.ShouldBe(state);
        summary.RevisionTriggerKind.ShouldBe(triggerKind);
    }

    [Theory]
    [InlineData("review")]
    [InlineData("checks_pending")]
    [InlineData("rebase_pending")]
    [InlineData("continuable_failed")]
    [InlineData("completed")]
    public async Task WhenNonRevisionState_RevisionTriggerKindIsNull(string state)
    {
        // Arrange
        Issue issue = BuildState(new IssueBuilder(), state);
        _dbContext.Set<Issue>().Add(issue);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        IssueSummary? result = await _sut.GetIssueSummaryAsync(issue.Id, TestContext.Current.CancellationToken);

        // Assert
        IssueSummary summary = result.ShouldNotBeNull();
        summary.State.ShouldBe(state);
        summary.RevisionTriggerKind.ShouldBeNull();
    }

    [Fact]
    public async Task WhenAwaitingInput_RevisionTriggerKindIsNull()
    {
        // Arrange
        AwaitingInputIssue issue = new IssueBuilder().AwaitingInput();
        _dbContext.Set<Issue>().Add(issue);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        IssueSummary? result = await _sut.GetIssueSummaryAsync(issue.Id, TestContext.Current.CancellationToken);

        // Assert
        IssueSummary summary = result.ShouldNotBeNull();
        summary.State.ShouldBe("awaiting_input");
        summary.RevisionTriggerKind.ShouldBeNull();
    }

    private static Issue BuildRevisionState(IssueBuilder builder, string triggerKind, string state)
    {
        RevisionQueuedIssue queued = triggerKind switch
        {
            "review_feedback" => builder.RevisionQueued(),
            "failed_checks" => builder.ChecksPending().FixChecks([new FailingCheck("ci/build", "failure", null)]),
            "rebase_conflict" => builder.RebasePending().ResolveConflict(),
            "input_provided" => (RevisionQueuedIssue)builder
                .AwaitingInputPosted(DateTimeOffset.UtcNow.AddMinutes(-5))
                .ProvideInput("Use SQLite.", DateTimeOffset.UtcNow),
            _ => throw new ArgumentOutOfRangeException(nameof(triggerKind), triggerKind, null),
        };

        return state switch
        {
            "revision_queued" => queued,
            "revision_in_progress" => queued.Claim(WorkerRunId.New()),
            "revision_failed" => queued.Claim(WorkerRunId.New())
                .MarkFailed("Container exited with code 1", FailureCategory.NonZeroExit, DateTimeOffset.UtcNow),
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
        };
    }

    private static Issue BuildState(IssueBuilder builder, string state) =>
        state switch
        {
            "review" => builder.Review(),
            "checks_pending" => builder.ChecksPending(),
            "rebase_pending" => builder.RebasePending(),
            "revision_queued" => builder.RevisionQueued(),
            "revision_in_progress" => builder.RevisionInProgress(),
            "revision_failed" => builder.RevisionFailed(),
            "continuable_failed" => builder.ContinuableFailedFromReview(),
            "completed" => builder.Completed(),
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
        };

    private sealed class StubWorkerRunQueries : IWorkerRunQueries
    {
        private readonly Dictionary<Guid, RunAggregate> _aggregates = [];
        private readonly Dictionary<Guid, WorkerRunPublishPhase> _publishPhases = [];

        public void SetAggregate(Guid issueId, RunAggregate aggregate)
        {
            _aggregates[issueId] = aggregate;
        }

        public Task<IReadOnlyDictionary<Guid, RunAggregate>> GetRunAggregatesForIssuesAsync(
            IReadOnlyCollection<Guid> issueIds,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyDictionary<Guid, RunAggregate>>(
                issueIds
                    .Where(_aggregates.ContainsKey)
                    .ToDictionary(id => id, id => _aggregates[id]));

        public Task<Result<WorkerRunDetail>> GetWorkerRunDetailAsync(
            Guid workerRunId,
            CancellationToken cancellationToken)
            => Task.FromResult(Result<WorkerRunDetail>.Fail(new Error("Test.NotFound", "Not found")));

        public Task<WorkerRunLogResult> GetWorkerRunLogAsync(
            Guid workerRunId,
            CancellationToken cancellationToken)
            => Task.FromResult<WorkerRunLogResult>(new WorkerRunLogResult.RunNotFound());

        public Task<RunTotals> GetRunTotalsAsync(
            DateTimeOffset from,
            DateTimeOffset to,
            CancellationToken cancellationToken)
            => Task.FromResult(new RunTotals(0, 0L, 0, 0m, TokenTotal.None, TokenTotal.None));

        public Task<int> CountConsecutiveTransientRunsAsync(
            Guid issueId,
            int maxAttempts,
            CancellationToken cancellationToken)
            => Task.FromResult(0);

        public Task<IReadOnlyCollection<WorkerActivity>> GetActiveRunActivityAsync(
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyCollection<WorkerActivity>>([]);

        public void SetPublishPhase(Guid issueId, WorkerRunPublishPhase phase)
        {
            _publishPhases[issueId] = phase;
        }

        public Task<IReadOnlyDictionary<Guid, WorkerRunPublishPhase>> GetPublishPhasesForIssuesAsync(
            IReadOnlyCollection<Guid> issueIds,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyDictionary<Guid, WorkerRunPublishPhase>>(
                issueIds
                    .Where(_publishPhases.ContainsKey)
                    .ToDictionary(id => id, id => _publishPhases[id]));
    }
}
