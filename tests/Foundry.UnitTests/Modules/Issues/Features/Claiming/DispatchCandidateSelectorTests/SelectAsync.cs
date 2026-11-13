using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Issues.Features.Claiming;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Contracts.Queries;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Features.Claiming.DispatchCandidateSelectorTests;

public sealed class SelectAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly CapturingDomainEventDispatcher _domainEventDispatcher = new();

    private static readonly RepositoryDispatchInfo DefaultDispatchInfo = new(
        "owner/repo",
        new Uri("https://github.com/owner/repo.git"),
        "GITHUB_PAT",
        new WorkerProvider.GitHub(),
        "https://api.github.com/repos/owner/repo/issues",
        "github.com");

    public SelectAsync()
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

    private DispatchCandidateSelector BuildSelector(
        IRepositoryEligibilityQuery? repositoryEligibilityQuery = null,
        IRepositoryDispatchQueries? repositoryDispatchQueries = null,
        IIssueBlockerQueries? issueBlockerQueries = null,
        IGlobalSettingsQueries? settingsQueries = null)
    {
        return new DispatchCandidateSelector(
            _dbContext,
            repositoryDispatchQueries ?? new StubRepositoryDispatchQueries(DefaultDispatchInfo),
            repositoryEligibilityQuery ?? new AllEligibleRepositoryEligibilityQuery(),
            new InFlightWorkerCountQuery(_dbContext),
            issueBlockerQueries ?? new StubIssueBlockerQueries(),
            IssueDependencyApplierFactory.Create(_dbContext, _domainEventDispatcher),
            settingsQueries ?? new NullGlobalSettingsQueries(),
            NullLogger<DispatchCandidateSelector>.Instance);
    }

    private FreshQueuedIssue SeedQueuedIssue(MonitoredRepositoryId repositoryId, int issueNumber = 1)
    {
        FreshQueuedIssue queued = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(issueNumber)
            .WithTitle($"Issue {issueNumber}")
            .FreshQueued();
        _dbContext.Set<Issue>().Add(queued);
        _dbContext.SaveChanges();
        _dbContext.ChangeTracker.Clear();
        return queued;
    }

    private FreshQueuedIssue SeedQueuedIssueAtTime(
        MonitoredRepositoryId repositoryId,
        int issueNumber,
        DateTimeOffset detectedAt)
    {
        FreshQueuedIssue queued = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(issueNumber)
            .WithTitle($"Issue {issueNumber}")
            .WithDetectedAt(detectedAt)
            .FreshQueued();
        _dbContext.Set<Issue>().Add(queued);
        _dbContext.SaveChanges();
        _dbContext.ChangeTracker.Clear();
        return queued;
    }

    // Cycle 1: happy path — returns Selected with the candidate and dispatch info
    [Fact]
    public async Task WhenCandidateExistsAndDispatchInfoResolves_ReturnsSelected()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        FreshQueuedIssue queued = SeedQueuedIssue(repositoryId);

        DispatchCandidateSelector sut = BuildSelector(
            repositoryEligibilityQuery: new StubRepositoryEligibilityQuery(
                [new EligibleRepository(repositoryId.Value, Position: 0, MaxConcurrentWorkers: 1, Host: "github.com")]));

        // Act
        SelectionOutcome outcome = await sut.SelectAsync(CancellationToken.None);

        // Assert
        SelectionOutcome.Selected selected = outcome.ShouldBeOfType<SelectionOutcome.Selected>();
        selected.Candidate.Issue.Id.ShouldBe(queued.Id);
        selected.Candidate.DispatchInfo.ShouldBe(DefaultDispatchInfo);
    }

    // Cycle 2: candidates exist but none of their repos are eligible — returns NoEligibleRepositories
    [Fact]
    public async Task WhenCandidatesExistButNoRepositoriesEligible_ReturnsNoEligibleRepositories()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedQueuedIssue(repositoryId);

        DispatchCandidateSelector sut = BuildSelector(
            repositoryEligibilityQuery: new StubRepositoryEligibilityQuery([]));

        // Act
        SelectionOutcome outcome = await sut.SelectAsync(CancellationToken.None);

        // Assert
        outcome.ShouldBeOfType<SelectionOutcome.NoEligibleRepositories>();
    }

    // Cycle 3: no candidates at all — returns NoCandidates
    [Fact]
    public async Task WhenNoCandidatesExist_ReturnsNoCandidates()
    {
        // Arrange
        // No QueuedIssue records in the database.
        DispatchCandidateSelector sut = BuildSelector();

        // Act
        SelectionOutcome outcome = await sut.SelectAsync(CancellationToken.None);

        // Assert
        outcome.ShouldBeOfType<SelectionOutcome.NoCandidates>();
    }

    // Cycle 4: all candidates' repos are unresolvable — returns AllCandidatesUnresolvable with count.
    // MaxConcurrentWorkers: 2 so both issues enter the capacity-aware ordered set; both are skipped
    // when dispatch info is null.
    [Fact]
    public async Task WhenAllCandidatesUnresolvable_ReturnsAllCandidatesUnresolvableWithSkipCount()
    {
        // Arrange
        MonitoredRepositoryId repoId = MonitoredRepositoryId.New();
        SeedQueuedIssue(repoId, issueNumber: 1);
        SeedQueuedIssue(repoId, issueNumber: 2);

        DispatchCandidateSelector sut = BuildSelector(
            repositoryEligibilityQuery: new StubRepositoryEligibilityQuery(
                [new EligibleRepository(repoId.Value, Position: 0, MaxConcurrentWorkers: 2, Host: "github.com")]),
            repositoryDispatchQueries: new StubRepositoryDispatchQueries(null));

        // Act
        SelectionOutcome outcome = await sut.SelectAsync(CancellationToken.None);

        // Assert
        SelectionOutcome.AllCandidatesUnresolvable unresolvable =
            outcome.ShouldBeOfType<SelectionOutcome.AllCandidatesUnresolvable>();
        unresolvable.Skipped.ShouldBe(2);
    }

    // Cycle 5: fall-through — head candidate's repo unresolvable, next-best is returned
    [Fact]
    public async Task WhenHeadCandidateRepoUnresolvable_ReturnsBestResolvableCandidate()
    {
        // Arrange
        MonitoredRepositoryId unresolvableRepoId = MonitoredRepositoryId.New();
        MonitoredRepositoryId resolvableRepoId = MonitoredRepositoryId.New();

        DateTimeOffset olderTime = DateTimeOffset.UtcNow.AddMinutes(-5);
        DateTimeOffset newerTime = DateTimeOffset.UtcNow;

        // Unresolvable repo has the lower issue number → lower DispatchOrderKey → heads the queue
        FreshQueuedIssue unresolvableIssue = SeedQueuedIssueAtTime(unresolvableRepoId, issueNumber: 1, detectedAt: olderTime);
        FreshQueuedIssue resolvableIssue = SeedQueuedIssueAtTime(resolvableRepoId, issueNumber: 2, detectedAt: newerTime);

        DispatchCandidateSelector sut = BuildSelector(
            repositoryEligibilityQuery: new StubRepositoryEligibilityQuery(
            [
                new EligibleRepository(unresolvableRepoId.Value, Position: 0, MaxConcurrentWorkers: 1, Host: "github.com"),
                new EligibleRepository(resolvableRepoId.Value, Position: 0, MaxConcurrentWorkers: 1, Host: "github.com"),
            ]),
            repositoryDispatchQueries: new SelectiveDispatchQueries(
                resolvable: resolvableRepoId,
                dispatchInfo: DefaultDispatchInfo));

        // Act
        SelectionOutcome outcome = await sut.SelectAsync(CancellationToken.None);

        // Assert
        SelectionOutcome.Selected selected = outcome.ShouldBeOfType<SelectionOutcome.Selected>();
        selected.Candidate.Issue.Id.ShouldBe(resolvableIssue.Id);
    }

    // Cycle 6: memoization — multiple candidates in the same unresolvable repo
    //          → GetDispatchInfoAsync called exactly ONCE for that repo
    [Fact]
    public async Task WhenMultipleCandidatesFromSameUnresolvableRepo_QueriesDispatchInfoOnce()
    {
        // Arrange
        MonitoredRepositoryId unresolvableRepoId = MonitoredRepositoryId.New();

        SeedQueuedIssue(unresolvableRepoId, issueNumber: 1);
        SeedQueuedIssue(unresolvableRepoId, issueNumber: 2);
        SeedQueuedIssue(unresolvableRepoId, issueNumber: 3);

        CountingDispatchQueries countingQueries = new(resolvable: null, dispatchInfo: null);

        DispatchCandidateSelector sut = BuildSelector(
            repositoryEligibilityQuery: new StubRepositoryEligibilityQuery(
                [new EligibleRepository(unresolvableRepoId.Value, Position: 0, MaxConcurrentWorkers: 1, Host: "github.com")]),
            repositoryDispatchQueries: countingQueries);

        // Act
        await sut.SelectAsync(CancellationToken.None);

        // Assert — dispatch info for the unresolvable repo queried exactly once (memoized)
        countingQueries.CallCount(unresolvableRepoId).ShouldBe(1);
    }

    // Cycle 7: all repositories saturated — queued issue exists but the repo is at capacity
    //          (MaxConcurrentWorkers=1, InProgress issue already running) → AllRepositoriesSaturated
    [Fact]
    public async Task WhenAllRepositoriesSaturated_ReturnsAllRepositoriesSaturated()
    {
        // Arrange
        MonitoredRepositoryId repoId = MonitoredRepositoryId.New();

        // Seed queued issue
        SeedQueuedIssue(repoId, issueNumber: 1);

        // Seed in-progress issue for same repo — consumes the single slot
        InProgressIssue inProgress = new IssueBuilder()
            .WithMonitoredRepositoryId(repoId)
            .WithIssueNumber(10)
            .InProgress();
        _dbContext.Set<Issue>().Add(inProgress);
        await _dbContext.SaveChangesAsync(CancellationToken.None);
        _dbContext.ChangeTracker.Clear();

        DispatchCandidateSelector sut = BuildSelector(
            repositoryEligibilityQuery: new StubRepositoryEligibilityQuery(
                [new EligibleRepository(repoId.Value, Position: 0, MaxConcurrentWorkers: 1, Host: "github.com")]));

        // Act
        SelectionOutcome outcome = await sut.SelectAsync(CancellationToken.None);

        // Assert
        outcome.ShouldBeOfType<SelectionOutcome.AllRepositoriesSaturated>();
    }

    // Cycle 8: guard — with total in-flight >= global max, the selector never claims beyond a
    // repository's MaxConcurrentWorkers. A repo with MaxConcurrentWorkers=1 and one in-flight issue
    // yields zero ForClaim headroom, so the queued issue in that repo is not claimable — even though
    // the global max has been lowered to 1 while a reservation is in flight.
    // This proves the selector uses ForClaim (per-repo cap only) and never leaks ForForecast behaviour.
    [Fact]
    public async Task WhenGlobalMaxSaturatedAndRepoAtCap_ReturnsAllRepositoriesSaturated()
    {
        // Arrange
        MonitoredRepositoryId repoId = MonitoredRepositoryId.New();

        // Seed the queued issue — this is what we hope the selector does NOT claim.
        SeedQueuedIssue(repoId, issueNumber: 1);

        // Seed in-progress issue — consumes the repo's single slot.
        // Total in-flight (1) >= hypothetical global max (1): the global slots are saturated.
        // ForForecast would give headroom 1 here (assuming one worker finishes).
        // ForClaim correctly gives headroom 0 (repo is at its cap right now).
        InProgressIssue inProgress = new IssueBuilder()
            .WithMonitoredRepositoryId(repoId)
            .WithIssueNumber(10)
            .InProgress();
        _dbContext.Set<Issue>().Add(inProgress);
        await _dbContext.SaveChangesAsync(CancellationToken.None);
        _dbContext.ChangeTracker.Clear();

        DispatchCandidateSelector sut = BuildSelector(
            repositoryEligibilityQuery: new StubRepositoryEligibilityQuery(
                [new EligibleRepository(repoId.Value, Position: 0, MaxConcurrentWorkers: 1, Host: "github.com")]));

        // Act
        SelectionOutcome outcome = await sut.SelectAsync(CancellationToken.None);

        // Assert — ForClaim headroom is 0 (repo at cap); the queued issue must not be claimed.
        // If the selector leaked ForForecast behaviour it would return Selected instead.
        outcome.ShouldBeOfType<SelectionOutcome.AllRepositoriesSaturated>();
    }

    // Claim-time blocker re-check (#731)
    [Fact]
    public async Task WhenFreshCandidateHasNoOpenBlockers_ReturnsSelectedAfterReadingBlockers()
    {
        // Arrange
        MonitoredRepositoryId repoId = MonitoredRepositoryId.New();
        FreshQueuedIssue queued = SeedQueuedIssue(repoId, issueNumber: 7);
        StubIssueBlockerQueries blockerQueries = new();

        DispatchCandidateSelector sut = BuildSelector(issueBlockerQueries: blockerQueries);

        // Act
        SelectionOutcome outcome = await sut.SelectAsync(CancellationToken.None);

        // Assert
        outcome.ShouldBeOfType<SelectionOutcome.Selected>().Candidate.Issue.Id.ShouldBe(queued.Id);
        blockerQueries.RequestedIssueNumbers.ShouldBe([7]);
    }

    [Fact]
    public async Task WhenHeadCandidateGainedBlocker_BlocksItAndSelectsNextSameRepositoryCandidate()
    {
        // Arrange — MaxConcurrentWorkers 1: issue 2 only fits once issue 1 releases its slot
        MonitoredRepositoryId repoId = MonitoredRepositoryId.New();
        SeedQueuedIssue(repoId, issueNumber: 1);
        FreshQueuedIssue next = SeedQueuedIssue(repoId, issueNumber: 2);
        StubIssueBlockerQueries blockerQueries = new StubIssueBlockerQueries().WithBlockers(1, 99);

        DispatchCandidateSelector sut = BuildSelector(
            repositoryEligibilityQuery: new StubRepositoryEligibilityQuery(
                [new EligibleRepository(repoId.Value, Position: 0, MaxConcurrentWorkers: 1, Host: "github.com")]),
            issueBlockerQueries: blockerQueries);

        // Act
        SelectionOutcome outcome = await sut.SelectAsync(CancellationToken.None);

        // Assert
        outcome.ShouldBeOfType<SelectionOutcome.Selected>().Candidate.Issue.Id.ShouldBe(next.Id);

        _dbContext.ChangeTracker.Clear();
        Issue blocked = await _dbContext.Set<Issue>()
            .SingleAsync(i => i.IssueNumber == 1, TestContext.Current.CancellationToken);
        blocked.ShouldBeOfType<BlockedIssue>().BlockedBy.ShouldBe([99]);
        _domainEventDispatcher.DispatchedEvents.OfType<IssueBlocked>().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task WhenEveryCandidateGainedBlocker_ReturnsAllCandidatesSkippedWithBlockedCount()
    {
        // Arrange
        MonitoredRepositoryId repoId = MonitoredRepositoryId.New();
        SeedQueuedIssue(repoId, issueNumber: 1);
        SeedQueuedIssue(repoId, issueNumber: 2);
        StubIssueBlockerQueries blockerQueries = new StubIssueBlockerQueries()
            .WithBlockers(1, 50)
            .WithBlockers(2, 50);

        DispatchCandidateSelector sut = BuildSelector(issueBlockerQueries: blockerQueries);

        // Act
        SelectionOutcome outcome = await sut.SelectAsync(CancellationToken.None);

        // Assert
        outcome.ShouldBe(new SelectionOutcome.AllCandidatesSkipped(
            Unresolvable: 0,
            Blocked: 2,
            BlockerReadFailed: 0));

        _dbContext.ChangeTracker.Clear();
        (await _dbContext.Set<QueuedIssue>().CountAsync(TestContext.Current.CancellationToken)).ShouldBe(0);
        (await _dbContext.Set<BlockedIssue>().CountAsync(TestContext.Current.CancellationToken)).ShouldBe(2);
    }

    [Fact]
    public async Task WhenBlockerReadFails_SkipsCandidateWithoutStateChangeAndSelectsNext()
    {
        // Arrange — two repositories so the failed candidate keeps its slot and the next is tried
        MonitoredRepositoryId failingRepoId = MonitoredRepositoryId.New();
        MonitoredRepositoryId otherRepoId = MonitoredRepositoryId.New();
        FreshQueuedIssue failing = SeedQueuedIssue(failingRepoId, issueNumber: 1);
        FreshQueuedIssue other = SeedQueuedIssue(otherRepoId, issueNumber: 2);
        StubIssueBlockerQueries blockerQueries = new StubIssueBlockerQueries().WithFailure(1);

        DispatchCandidateSelector sut = BuildSelector(
            repositoryEligibilityQuery: new StubRepositoryEligibilityQuery(
            [
                new EligibleRepository(failingRepoId.Value, Position: 0, MaxConcurrentWorkers: 1, Host: "github.com"),
                new EligibleRepository(otherRepoId.Value, Position: 1, MaxConcurrentWorkers: 1, Host: "github.com"),
            ]),
            issueBlockerQueries: blockerQueries);

        // Act
        SelectionOutcome outcome = await sut.SelectAsync(CancellationToken.None);

        // Assert
        outcome.ShouldBeOfType<SelectionOutcome.Selected>().Candidate.Issue.Id.ShouldBe(other.Id);

        _dbContext.ChangeTracker.Clear();
        Issue unchanged = await _dbContext.Set<Issue>()
            .SingleAsync(i => i.Id == failing.Id, TestContext.Current.CancellationToken);
        unchanged.ShouldBeOfType<FreshQueuedIssue>();
        _domainEventDispatcher.DispatchedEvents.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenEveryBlockerReadFails_ReturnsAllCandidatesSkippedWithReadFailedCount()
    {
        // Arrange
        MonitoredRepositoryId repoId = MonitoredRepositoryId.New();
        SeedQueuedIssue(repoId, issueNumber: 1);
        StubIssueBlockerQueries blockerQueries = new StubIssueBlockerQueries().WithFailure(1);

        DispatchCandidateSelector sut = BuildSelector(issueBlockerQueries: blockerQueries);

        // Act
        SelectionOutcome outcome = await sut.SelectAsync(CancellationToken.None);

        // Assert
        outcome.ShouldBe(new SelectionOutcome.AllCandidatesSkipped(
            Unresolvable: 0,
            Blocked: 0,
            BlockerReadFailed: 1));
    }

    private static PausedHost PausedHostFor(string host) =>
        new(host, DateTimeOffset.UtcNow, ProbeAttempts: 0, DateTimeOffset.UtcNow.AddMinutes(1));

    [Fact]
    public async Task WhenEveryQueuedIssueIsOnAPausedHost_ReturnsNoCandidates()
    {
        // Arrange
        MonitoredRepositoryId repoId = MonitoredRepositoryId.New();
        SeedQueuedIssue(repoId);

        DispatchCandidateSelector sut = BuildSelector(
            repositoryEligibilityQuery: new StubRepositoryEligibilityQuery(
                [new EligibleRepository(repoId.Value, Position: 0, MaxConcurrentWorkers: 1, Host: "gitlab.com")]),
            settingsQueries: new PausedHostsGlobalSettingsQueries(PausedHostFor("gitlab.com")));

        // Act
        SelectionOutcome outcome = await sut.SelectAsync(CancellationToken.None);

        // Assert
        outcome.ShouldBeOfType<SelectionOutcome.NoCandidates>();
    }

    [Fact]
    public async Task WhenOneHostIsPaused_SelectsAnIssueOnAnotherHost()
    {
        // Arrange — the paused host's repository has the higher priority, so only the gate keeps it from winning.
        MonitoredRepositoryId pausedRepoId = MonitoredRepositoryId.New();
        MonitoredRepositoryId otherRepoId = MonitoredRepositoryId.New();
        SeedQueuedIssue(pausedRepoId, issueNumber: 1);
        FreshQueuedIssue other = SeedQueuedIssue(otherRepoId, issueNumber: 2);

        DispatchCandidateSelector sut = BuildSelector(
            repositoryEligibilityQuery: new StubRepositoryEligibilityQuery(
            [
                new EligibleRepository(pausedRepoId.Value, Position: 0, MaxConcurrentWorkers: 1, Host: "gitlab.com"),
                new EligibleRepository(otherRepoId.Value, Position: 1, MaxConcurrentWorkers: 1, Host: "github.com"),
            ]),
            settingsQueries: new PausedHostsGlobalSettingsQueries(PausedHostFor("gitlab.com")));

        // Act
        SelectionOutcome outcome = await sut.SelectAsync(CancellationToken.None);

        // Assert
        outcome.ShouldBeOfType<SelectionOutcome.Selected>().Candidate.Issue.Id.ShouldBe(other.Id);
    }

    [Fact]
    public async Task WhenARevisionIsQueuedOnAPausedHost_ReturnsNoCandidates()
    {
        // Arrange
        MonitoredRepositoryId repoId = MonitoredRepositoryId.New();
        RevisionQueuedIssue revision = new IssueBuilder()
            .WithMonitoredRepositoryId(repoId)
            .WithIssueNumber(3)
            .RevisionQueued();
        _dbContext.Set<Issue>().Add(revision);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        DispatchCandidateSelector sut = BuildSelector(
            repositoryEligibilityQuery: new StubRepositoryEligibilityQuery(
                [new EligibleRepository(repoId.Value, Position: 0, MaxConcurrentWorkers: 1, Host: "gitlab.com")]),
            settingsQueries: new PausedHostsGlobalSettingsQueries(PausedHostFor("GitLab.com")));

        // Act
        SelectionOutcome outcome = await sut.SelectAsync(CancellationToken.None);

        // Assert
        outcome.ShouldBeOfType<SelectionOutcome.NoCandidates>();
    }

    [Fact]
    public async Task WhenCandidateIsRevisionQueued_DoesNotReadBlockers()
    {
        // Arrange
        MonitoredRepositoryId repoId = MonitoredRepositoryId.New();
        RevisionQueuedIssue revision = new IssueBuilder()
            .WithMonitoredRepositoryId(repoId)
            .WithIssueNumber(3)
            .RevisionQueued();
        _dbContext.Set<Issue>().Add(revision);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        StubIssueBlockerQueries blockerQueries = new StubIssueBlockerQueries().WithBlockers(3, 1);

        DispatchCandidateSelector sut = BuildSelector(issueBlockerQueries: blockerQueries);

        // Act
        SelectionOutcome outcome = await sut.SelectAsync(CancellationToken.None);

        // Assert
        outcome.ShouldBeOfType<SelectionOutcome.Selected>().Candidate.Issue.Id.ShouldBe(revision.Id);
        blockerQueries.RequestedIssueNumbers.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenClaimTimeBlockerFormsCycle_BlocksCandidateAndRaisesCircularDependency()
    {
        // Arrange — issue 1 is already blocked by 2; issue 2 now reports 1 as a blocker
        MonitoredRepositoryId repoId = MonitoredRepositoryId.New();
        BlockedIssue existing = new IssueBuilder()
            .WithMonitoredRepositoryId(repoId)
            .WithIssueNumber(1)
            .Detected()
            .Block([2]);
        _dbContext.Set<Issue>().Add(existing);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();
        SeedQueuedIssue(repoId, issueNumber: 2);

        StubIssueBlockerQueries blockerQueries = new StubIssueBlockerQueries().WithBlockers(2, 1);

        DispatchCandidateSelector sut = BuildSelector(issueBlockerQueries: blockerQueries);

        // Act
        SelectionOutcome outcome = await sut.SelectAsync(CancellationToken.None);

        // Assert
        outcome.ShouldBeOfType<SelectionOutcome.AllCandidatesSkipped>().Blocked.ShouldBe(1);

        _dbContext.ChangeTracker.Clear();
        Issue issue = await _dbContext.Set<Issue>()
            .SingleAsync(i => i.IssueNumber == 2, TestContext.Current.CancellationToken);
        issue.ShouldBeOfType<BlockedIssue>().BlockedBy.ShouldBe([1]);
        _domainEventDispatcher.DispatchedEvents.OfType<CircularDependencyDetected>().ShouldHaveSingleItem();
    }

    private TriageQueuedIssue SeedTriageQueuedIssue(MonitoredRepositoryId repositoryId, int issueNumber)
    {
        TriageQueuedIssue queued = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(issueNumber)
            .WithTitle($"Issue {issueNumber}")
            .TriageQueued();
        _dbContext.Set<Issue>().Add(queued);
        _dbContext.SaveChanges();
        _dbContext.ChangeTracker.Clear();
        return queued;
    }

    // Claim-time blocker re-check for triage candidates (ADR 0102)
    [Fact]
    public async Task WhenTriageCandidateHasNoOpenBlockers_ReturnsSelectedAfterReadingBlockers()
    {
        // Arrange
        MonitoredRepositoryId repoId = MonitoredRepositoryId.New();
        TriageQueuedIssue queued = SeedTriageQueuedIssue(repoId, issueNumber: 7);
        StubIssueBlockerQueries blockerQueries = new();

        DispatchCandidateSelector sut = BuildSelector(issueBlockerQueries: blockerQueries);

        // Act
        SelectionOutcome outcome = await sut.SelectAsync(CancellationToken.None);

        // Assert
        outcome.ShouldBeOfType<SelectionOutcome.Selected>().Candidate.Issue.Id.ShouldBe(queued.Id);
        blockerQueries.RequestedIssueNumbers.ShouldBe([7]);
    }

    [Fact]
    public async Task WhenTriageCandidateGainedBlocker_WaitsUntriagedAndSelectsNextSameRepositoryCandidate()
    {
        // Arrange — MaxConcurrentWorkers 1: issue 2 only fits once issue 1 releases its slot
        MonitoredRepositoryId repoId = MonitoredRepositoryId.New();
        SeedTriageQueuedIssue(repoId, issueNumber: 1);
        TriageQueuedIssue next = SeedTriageQueuedIssue(repoId, issueNumber: 2);
        StubIssueBlockerQueries blockerQueries = new StubIssueBlockerQueries().WithBlockers(1, 99);

        DispatchCandidateSelector sut = BuildSelector(
            repositoryEligibilityQuery: new StubRepositoryEligibilityQuery(
                [new EligibleRepository(repoId.Value, Position: 0, MaxConcurrentWorkers: 1, Host: "github.com")]),
            issueBlockerQueries: blockerQueries);

        // Act
        SelectionOutcome outcome = await sut.SelectAsync(CancellationToken.None);

        // Assert
        outcome.ShouldBeOfType<SelectionOutcome.Selected>().Candidate.Issue.Id.ShouldBe(next.Id);

        _dbContext.ChangeTracker.Clear();
        Issue blocked = await _dbContext.Set<Issue>()
            .SingleAsync(i => i.IssueNumber == 1, TestContext.Current.CancellationToken);
        blocked.ShouldBeOfType<TriageBlockedIssue>().BlockedBy.ShouldBe([99]);
        _domainEventDispatcher.DispatchedEvents.OfType<IssueTriageBlocked>().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task WhenEveryTriageCandidateGainedBlocker_ReturnsAllCandidatesSkippedWithBlockedCount()
    {
        // Arrange
        MonitoredRepositoryId repoId = MonitoredRepositoryId.New();
        SeedTriageQueuedIssue(repoId, issueNumber: 1);
        StubIssueBlockerQueries blockerQueries = new StubIssueBlockerQueries().WithBlockers(1, 50);

        DispatchCandidateSelector sut = BuildSelector(issueBlockerQueries: blockerQueries);

        // Act
        SelectionOutcome outcome = await sut.SelectAsync(CancellationToken.None);

        // Assert
        outcome.ShouldBe(new SelectionOutcome.AllCandidatesSkipped(
            Unresolvable: 0,
            Blocked: 1,
            BlockerReadFailed: 0));
    }

    [Fact]
    public async Task WhenTriageCandidateBlockerReadFails_SkipsItWithoutStateChange()
    {
        // Arrange
        MonitoredRepositoryId repoId = MonitoredRepositoryId.New();
        TriageQueuedIssue queued = SeedTriageQueuedIssue(repoId, issueNumber: 1);
        StubIssueBlockerQueries blockerQueries = new StubIssueBlockerQueries().WithFailure(1);

        DispatchCandidateSelector sut = BuildSelector(issueBlockerQueries: blockerQueries);

        // Act
        SelectionOutcome outcome = await sut.SelectAsync(CancellationToken.None);

        // Assert
        outcome.ShouldBe(new SelectionOutcome.AllCandidatesSkipped(
            Unresolvable: 0,
            Blocked: 0,
            BlockerReadFailed: 1));

        _dbContext.ChangeTracker.Clear();
        Issue unchanged = await _dbContext.Set<Issue>()
            .SingleAsync(i => i.Id == queued.Id, TestContext.Current.CancellationToken);
        unchanged.ShouldBeOfType<TriageQueuedIssue>();
        _domainEventDispatcher.DispatchedEvents.ShouldBeEmpty();
    }

    // Stub that returns exactly the provided eligible repositories (with positions).
    private sealed class StubRepositoryEligibilityQuery(IReadOnlyCollection<EligibleRepository> eligibleRepositories)
        : IRepositoryEligibilityQuery
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
            => Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
    }

    // Stub that marks every queried repository as eligible with default position 0.
    private sealed class AllEligibleRepositoryEligibilityQuery : IRepositoryEligibilityQuery
    {
        public Task<RepositoryEligibilityInfo?> GetEligibilityAsync(
            Guid repositoryId,
            CancellationToken cancellationToken)
            => Task.FromResult<RepositoryEligibilityInfo?>(null);

        public Task<IReadOnlyList<EligibleRepository>> GetEligibleRepositoriesAsync(
            IReadOnlyCollection<Guid> repositoryIds,
            CancellationToken cancellationToken)
        {
            IReadOnlyList<EligibleRepository> eligible = repositoryIds
                .Select(id => new EligibleRepository(id, Position: 0, MaxConcurrentWorkers: 1, Host: "github.com"))
                .ToList();
            return Task.FromResult(eligible);
        }

        public Task<IReadOnlyDictionary<Guid, string>> GetEligibilityStatusesAsync(
            IReadOnlyCollection<Guid> repositoryIds,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
    }

    // Stub that always returns the provided dispatch info (or null).
    private sealed class StubRepositoryDispatchQueries(RepositoryDispatchInfo? info) : IRepositoryDispatchQueries
    {
        public Task<RepositoryDispatchInfo?> GetDispatchInfoAsync(
            MonitoredRepositoryId repositoryId,
            CancellationToken cancellationToken)
            => Task.FromResult(info);
    }

    // Returns dispatch info only for the specified resolvable repository; null for all others.
    private sealed class SelectiveDispatchQueries(
        MonitoredRepositoryId resolvable,
        RepositoryDispatchInfo dispatchInfo) : IRepositoryDispatchQueries
    {
        public Task<RepositoryDispatchInfo?> GetDispatchInfoAsync(
            MonitoredRepositoryId repositoryId,
            CancellationToken cancellationToken)
        {
            RepositoryDispatchInfo? result = repositoryId == resolvable ? dispatchInfo : null;
            return Task.FromResult(result);
        }
    }

    // Counting stub that records how many times GetDispatchInfoAsync is called per repository.
    private sealed class CountingDispatchQueries(
        MonitoredRepositoryId? resolvable,
        RepositoryDispatchInfo? dispatchInfo) : IRepositoryDispatchQueries
    {
        private readonly Dictionary<MonitoredRepositoryId, int> _callCounts = [];

        public int CallCount(MonitoredRepositoryId repositoryId) =>
            _callCounts.TryGetValue(repositoryId, out int count) ? count : 0;

        public Task<RepositoryDispatchInfo?> GetDispatchInfoAsync(
            MonitoredRepositoryId repositoryId,
            CancellationToken cancellationToken)
        {
            _callCounts[repositoryId] = CallCount(repositoryId) + 1;
            RepositoryDispatchInfo? result = repositoryId == resolvable ? dispatchInfo : null;
            return Task.FromResult(result);
        }
    }
}
