using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Features;
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

/// <summary>
/// Tests for the queued-issue Dispatch Order partition in GetActiveIssueSummariesAsync.
/// Eligible-repo queued issues (in Dispatch Order) come before ineligible-repo queued issues
/// (also in Dispatch Order, with sentinel position). Non-queued active states follow separately.
/// </summary>
public sealed class GetActiveIssueSummariesAsync_QueueOrdering : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;

    private static readonly DateTimeOffset Now = new(2026, 6, 26, 12, 0, 0, TimeSpan.Zero);

    public GetActiveIssueSummariesAsync_QueueOrdering()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new FoundryDbContext(options);
        _dbContext.Database.EnsureCreated();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private IIssueQueries BuildSut(IRepositoryEligibilityQuery eligibilityQuery) =>
        new IssueQueries(_dbContext, new NullRepositorySlugQueries(), eligibilityQuery, new NullWorkerRunQueries());

    private FreshQueuedIssue SeedQueuedIssue(
        MonitoredRepositoryId repositoryId,
        int issueNumber,
        DateTimeOffset? detectedAt = null)
    {
        FreshQueuedIssue queued = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(issueNumber)
            .WithTitle($"Issue {issueNumber}")
            .WithDetectedAt(detectedAt ?? Now)
            .FreshQueued();
        _dbContext.Set<Issue>().Add(queued);
        _dbContext.SaveChanges();
        _dbContext.ChangeTracker.Clear();
        return queued;
    }

    private RevisionQueuedIssue SeedRevisionQueuedIssue(
        MonitoredRepositoryId repositoryId,
        int issueNumber,
        DateTimeOffset? detectedAt = null)
    {
        RevisionQueuedIssue revisionQueued = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(issueNumber)
            .WithTitle($"Issue {issueNumber}")
            .WithDetectedAt(detectedAt ?? Now)
            .WithBranchName($"feat/issue-{issueNumber}")
            .WithPullRequestUrl($"https://github.com/owner/repo/pull/{issueNumber}")
            .WithFeedbackCutoffAt(Now)
            .WithReviewComments([new ReviewComment("Please revise.")])
            .RevisionQueued();
        _dbContext.Set<Issue>().Add(revisionQueued);
        _dbContext.SaveChanges();
        _dbContext.ChangeTracker.Clear();
        return revisionQueued;
    }

    private ContinuationQueuedIssue SeedContinuationQueuedIssue(
        MonitoredRepositoryId repositoryId,
        int issueNumber,
        DateTimeOffset? detectedAt = null)
    {
        ContinuableFailedIssue continuableFailed = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(issueNumber)
            .WithTitle($"Issue {issueNumber}")
            .WithDetectedAt(detectedAt ?? Now)
            .WithBranchName($"feat/issue-{issueNumber}")
            .WithFailureReason("Non-zero exit code: 1")
            .WithFailureCategory(FailureCategory.NonZeroExit)
            .WithFailedAt(Now)
            .ContinuableFailed();
        ContinuationQueuedIssue continuationQueued = continuableFailed.Retry();
        _dbContext.Set<Issue>().Add(continuationQueued);
        _dbContext.SaveChanges();
        _dbContext.ChangeTracker.Clear();
        return continuationQueued;
    }

    private DetectedIssue SeedDetectedIssue(
        MonitoredRepositoryId repositoryId,
        int issueNumber,
        DateTimeOffset? detectedAt = null)
    {
        DetectedIssue detected = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(issueNumber)
            .WithTitle($"Issue {issueNumber}")
            .WithDetectedAt(detectedAt ?? Now)
            .Detected();
        _dbContext.Set<Issue>().Add(detected);
        _dbContext.SaveChanges();
        _dbContext.ChangeTracker.Clear();
        return detected;
    }

    [Fact]
    public async Task WhenQueuedIssuesAcrossTiers_OrderedByDispatchKey_TierRankFirst()
    {
        // Arrange: RevisionQueued (rank 0) should come before ContinuationQueued (rank 1)
        // and FreshQueuedIssue (rank 2), even if detected later.
        MonitoredRepositoryId repoA = MonitoredRepositoryId.New();

        SeedQueuedIssue(repoA, issueNumber: 1, detectedAt: Now.AddHours(-3));
        SeedContinuationQueuedIssue(repoA, issueNumber: 2, detectedAt: Now.AddHours(-2));
        SeedRevisionQueuedIssue(repoA, issueNumber: 3, detectedAt: Now.AddHours(-1));

        StubEligibilityQuery eligibilityQuery = new(
            [new EligibleRepository(repoA.Value, Position: 1)],
            new Dictionary<Guid, string>());

        IIssueQueries sut = BuildSut(eligibilityQuery);

        // Act
        IReadOnlyList<IssueSummary> result = await sut.GetActiveIssueSummariesAsync(
            repositoryId: null,
            states: null,
            TestContext.Current.CancellationToken);

        // Assert — Dispatch Order: revision_queued (rank 0), continuation_queued (rank 1), queued (rank 2)
        result.Count.ShouldBe(3);
        result[0].State.ShouldBe("revision_queued");
        result[1].State.ShouldBe("continuation_queued");
        result[2].State.ShouldBe("queued");
    }

    [Fact]
    public async Task WhenQueuedIssuesSameTierDifferentRepoPositions_OrderedByPosition()
    {
        // Arrange: two QueuedIssues on repos with different positions — lower position first.
        MonitoredRepositoryId repoAtPosition2 = MonitoredRepositoryId.New();
        MonitoredRepositoryId repoAtPosition1 = MonitoredRepositoryId.New();

        // Both detected at the same time so tie-break is only position.
        SeedQueuedIssue(repoAtPosition2, issueNumber: 10, detectedAt: Now);
        SeedQueuedIssue(repoAtPosition1, issueNumber: 20, detectedAt: Now);

        StubEligibilityQuery eligibilityQuery = new(
            [
                new EligibleRepository(repoAtPosition2.Value, Position: 2),
                new EligibleRepository(repoAtPosition1.Value, Position: 1),
            ],
            new Dictionary<Guid, string>());

        IIssueQueries sut = BuildSut(eligibilityQuery);

        // Act
        IReadOnlyList<IssueSummary> result = await sut.GetActiveIssueSummariesAsync(
            repositoryId: null,
            states: null,
            TestContext.Current.CancellationToken);

        // Assert — repo with position 1 comes before repo with position 2.
        result.Count.ShouldBe(2);
        result[0].IssueNumber.ShouldBe(20);
        result[1].IssueNumber.ShouldBe(10);
    }

    [Fact]
    public async Task WhenQueuedIssuesSameTierAndPosition_OrderedByDetectedAtAscending()
    {
        // Arrange: two QueuedIssues in same repo (same position), older one dispatched first.
        MonitoredRepositoryId repo = MonitoredRepositoryId.New();
        DateTimeOffset older = Now.AddHours(-2);
        DateTimeOffset newer = Now.AddHours(-1);

        SeedQueuedIssue(repo, issueNumber: 1, detectedAt: newer);
        SeedQueuedIssue(repo, issueNumber: 2, detectedAt: older);

        StubEligibilityQuery eligibilityQuery = new(
            [new EligibleRepository(repo.Value, Position: 1)],
            new Dictionary<Guid, string>());

        IIssueQueries sut = BuildSut(eligibilityQuery);

        // Act
        IReadOnlyList<IssueSummary> result = await sut.GetActiveIssueSummariesAsync(
            repositoryId: null,
            states: null,
            TestContext.Current.CancellationToken);

        // Assert — older detected first = lower DetectedAt value = dispatched first.
        result.Count.ShouldBe(2);
        result[0].IssueNumber.ShouldBe(2);
        result[1].IssueNumber.ShouldBe(1);
    }

    [Fact]
    public async Task WhenEligibleAndIneligibleQueuedIssues_EligibleComeFirst()
    {
        // Arrange: one queued on an eligible repo, one queued on an ineligible repo.
        MonitoredRepositoryId eligibleRepo = MonitoredRepositoryId.New();
        MonitoredRepositoryId ineligibleRepo = MonitoredRepositoryId.New();

        // Ineligible issue detected earlier — but eligible should still come first.
        SeedQueuedIssue(ineligibleRepo, issueNumber: 1, detectedAt: Now.AddHours(-10));
        SeedQueuedIssue(eligibleRepo, issueNumber: 2, detectedAt: Now.AddHours(-1));

        StubEligibilityQuery eligibilityQuery = new(
            [new EligibleRepository(eligibleRepo.Value, Position: 1)],
            new Dictionary<Guid, string>
            {
                [ineligibleRepo.Value] = "ineligible",
            });

        IIssueQueries sut = BuildSut(eligibilityQuery);

        // Act
        IReadOnlyList<IssueSummary> result = await sut.GetActiveIssueSummariesAsync(
            repositoryId: null,
            states: null,
            TestContext.Current.CancellationToken);

        // Assert — eligible queued (issue 2) precedes ineligible queued (issue 1).
        result.Count.ShouldBe(2);
        result[0].IssueNumber.ShouldBe(2);
        result[1].IssueNumber.ShouldBe(1);
    }

    [Fact]
    public async Task WhenIneligibleQueuedIssues_SortDeterministicallyByDetectedAtThenId()
    {
        // Arrange: two ineligible-repo queued issues — sentinel position, sort by DetectedAt then Id.
        MonitoredRepositoryId ineligibleRepo = MonitoredRepositoryId.New();
        DateTimeOffset older = Now.AddHours(-3);
        DateTimeOffset newer = Now.AddHours(-1);

        SeedQueuedIssue(ineligibleRepo, issueNumber: 1, detectedAt: newer);
        SeedQueuedIssue(ineligibleRepo, issueNumber: 2, detectedAt: older);

        StubEligibilityQuery eligibilityQuery = new(
            [],
            new Dictionary<Guid, string>
            {
                [ineligibleRepo.Value] = "ineligible",
            });

        IIssueQueries sut = BuildSut(eligibilityQuery);

        // Act
        IReadOnlyList<IssueSummary> result = await sut.GetActiveIssueSummariesAsync(
            repositoryId: null,
            states: null,
            TestContext.Current.CancellationToken);

        // Assert — older detected first (lower DetectedAt = dispatched first among ineligibles).
        result.Count.ShouldBe(2);
        result[0].IssueNumber.ShouldBe(2);
        result[1].IssueNumber.ShouldBe(1);
    }

    [Fact]
    public async Task WhenIneligibleQueuedIssue_RetainsRepositoryEligibilityStatus()
    {
        // Arrange
        MonitoredRepositoryId ineligibleRepo = MonitoredRepositoryId.New();
        SeedQueuedIssue(ineligibleRepo, issueNumber: 1);

        StubEligibilityQuery eligibilityQuery = new(
            [],
            new Dictionary<Guid, string>
            {
                [ineligibleRepo.Value] = "missing_credentials",
            });

        IIssueQueries sut = BuildSut(eligibilityQuery);

        // Act
        IReadOnlyList<IssueSummary> result = await sut.GetActiveIssueSummariesAsync(
            repositoryId: null,
            states: null,
            TestContext.Current.CancellationToken);

        // Assert
        result.ShouldHaveSingleItem();
        result[0].RepositoryEligibilityStatus.ShouldBe("missing_credentials");
    }

    [Fact]
    public async Task WhenNonQueuedAndQueuedActiveIssues_QueuedComeFirstNonQueuedNotInterleaved()
    {
        // Arrange: a detected (non-queued) and a queued issue on the same repo.
        // The queued issue must precede the non-queued, regardless of DetectedAt.
        MonitoredRepositoryId repo = MonitoredRepositoryId.New();

        // Detected issue is older (would come first under pure DetectedAt DESC).
        SeedDetectedIssue(repo, issueNumber: 1, detectedAt: Now.AddHours(-10));
        SeedQueuedIssue(repo, issueNumber: 2, detectedAt: Now.AddHours(-1));

        StubEligibilityQuery eligibilityQuery = new(
            [new EligibleRepository(repo.Value, Position: 1)],
            new Dictionary<Guid, string>());

        IIssueQueries sut = BuildSut(eligibilityQuery);

        // Act
        IReadOnlyList<IssueSummary> result = await sut.GetActiveIssueSummariesAsync(
            repositoryId: null,
            states: null,
            TestContext.Current.CancellationToken);

        // Assert — queued comes first, then non-queued.
        result.Count.ShouldBe(2);
        result[0].State.ShouldBe("queued");
        result[1].State.ShouldBe("detected");
    }

    [Fact]
    public async Task WhenNonQueuedIssuesOnly_OrderedByDetectedAtDescending()
    {
        // Arrange
        MonitoredRepositoryId repo = MonitoredRepositoryId.New();
        DateTimeOffset earlier = Now.AddHours(-2);
        DateTimeOffset later = Now.AddHours(-1);

        SeedDetectedIssue(repo, issueNumber: 1, detectedAt: earlier);
        SeedDetectedIssue(repo, issueNumber: 2, detectedAt: later);

        IIssueQueries sut = BuildSut(new NullRepositoryEligibilityQuery());

        // Act
        IReadOnlyList<IssueSummary> result = await sut.GetActiveIssueSummariesAsync(
            repositoryId: null,
            states: null,
            TestContext.Current.CancellationToken);

        // Assert
        result.Count.ShouldBe(2);
        result[0].IssueNumber.ShouldBe(2);
        result[1].IssueNumber.ShouldBe(1);
    }

    /// <summary>
    /// Returns the configured set of eligible repositories (with positions) and eligibility statuses.
    /// </summary>
    private sealed class StubEligibilityQuery(
        IReadOnlyList<EligibleRepository> eligibleRepositories,
        IReadOnlyDictionary<Guid, string> eligibilityStatuses) : IRepositoryEligibilityQuery
    {
        public Task<RepositoryEligibilityInfo?> GetEligibilityAsync(
            Guid repositoryId,
            CancellationToken cancellationToken)
            => Task.FromResult<RepositoryEligibilityInfo?>(null);

        public Task<IReadOnlyList<EligibleRepository>> GetEligibleRepositoriesAsync(
            IReadOnlyCollection<Guid> repositoryIds,
            CancellationToken cancellationToken)
        {
            IReadOnlyList<EligibleRepository> eligible = eligibleRepositories
                .Where(r => repositoryIds.Contains(r.Id))
                .ToList();
            return Task.FromResult(eligible);
        }

        public Task<IReadOnlyDictionary<Guid, string>> GetEligibilityStatusesAsync(
            IReadOnlyCollection<Guid> repositoryIds,
            CancellationToken cancellationToken)
            => Task.FromResult(eligibilityStatuses);
    }
}
