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
    }
}
