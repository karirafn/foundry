using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Issues.Features.Claiming;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Contracts.Queries;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Features.Claiming.DispatchCandidateSelectorTests;

/// <summary>
/// An issue with open blockers starts no implement run: whichever path queued a revision or a continuation, the
/// claim-time re-check holds it in its blocked state instead of selecting it.
/// </summary>
public sealed class RevisionAndContinuationGate : IAsyncDisposable
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

    public RevisionAndContinuationGate()
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
        IDispatchPauseQueries? dispatchPauseQueries = null,
        IPostExitProviderQueries? branchWorkQueries = null)
    {
        return new DispatchCandidateSelector(
            _dbContext,
            repositoryDispatchQueries ?? new StubRepositoryDispatchQueries(DefaultDispatchInfo),
            repositoryEligibilityQuery ?? new AllEligibleRepositoryEligibilityQuery(),
            new InFlightWorkerCountQuery(_dbContext, new NullWorkerRunQueries()),
            ClaimTimeRecheckFactory.Create(
                _dbContext,
                issueBlockerQueries ?? new StubIssueBlockerQueries(),
                _domainEventDispatcher,
                branchWorkQueries),
            dispatchPauseQueries ?? new NullDispatchPauseQueries(),
            NullLogger<DispatchCandidateSelector>.Instance);
    }

    private static readonly DateTimeOffset Now = new(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);

    private const int IssueNumber = 7;

    private const int Blocker = 99;

    private static IssueBuilder Builder(MonitoredRepositoryId repositoryId) => new IssueBuilder()
        .WithMonitoredRepositoryId(repositoryId)
        .WithIssueNumber(IssueNumber)
        .WithBranchName("feat/7-held")
        .WithPullRequestUrl("https://github.com/owner/repo/pull/70");

    private void Seed(Issue issue)
    {
        _dbContext.Set<Issue>().Add(issue);
        _dbContext.SaveChanges();
        _dbContext.ChangeTracker.Clear();
    }

    private async Task<Issue> ReloadAsync()
    {
        _dbContext.ChangeTracker.Clear();
        return await _dbContext.Set<Issue>()
            .SingleAsync(i => i.IssueNumber == IssueNumber, TestContext.Current.CancellationToken);
    }

    public static TheoryData<string> RevisionPaths() =>
    [
        "review_feedback",
        "review_findings",
        "failed_checks",
        "rebase_conflict",
        "input_provided",
        "retry_from_revision_failed",
    ];

    private static RevisionQueuedIssue QueueRevision(string path, MonitoredRepositoryId repositoryId) => path switch
    {
        "review_feedback" => Builder(repositoryId).RevisionQueued(),
        "review_findings" => Builder(repositoryId).RevisionQueuedFromReviewFindings(),
        "failed_checks" => Builder(repositoryId).ChecksPending()
            .FixChecks([new FailingCheck("ci/build", "failure", null)], Now),
        "rebase_conflict" => Builder(repositoryId).RebasePending().ResolveConflict(Now),
        "input_provided" => (RevisionQueuedIssue)Builder(repositoryId)
            .AwaitingInputPosted(Now.AddMinutes(-5))
            .ProvideInput(ClarificationInput.FromComments("Use SQLite."), Now, Now),
        "retry_from_revision_failed" => Builder(repositoryId).RevisionFailed().Retry(Now),
        _ => throw new ArgumentOutOfRangeException(nameof(path), path, null),
    };

    [Theory]
    [MemberData(nameof(RevisionPaths))]
    public async Task WhenAQueuedRevisionHasAnOpenBlocker_ItIsHeldAndNothingIsSelected(string path)
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        RevisionQueuedIssue queued = QueueRevision(path, repositoryId);
        Seed(queued);
        StubIssueBlockerQueries blockerQueries = new StubIssueBlockerQueries().WithBlockers(IssueNumber, Blocker);
        DispatchCandidateSelector sut = BuildSelector(issueBlockerQueries: blockerQueries);

        // Act
        SelectionOutcome outcome = await sut.SelectAsync(CancellationToken.None);

        // Assert
        outcome.ShouldBe(new SelectionOutcome.AllCandidatesSkipped(
            Unresolvable: 0,
            Blocked: 1,
            ReadFailed: 0,
            BranchAdopted: 0));
        RevisionBlockedIssue held = (await ReloadAsync()).ShouldBeOfType<RevisionBlockedIssue>();
        held.ShouldSatisfyAllConditions(
            () => held.BlockedBy.ShouldBe([Blocker]),
            () => held.Trigger.Discriminator.ShouldBe(queued.Trigger.Discriminator),
            () => held.BranchName.ShouldBe(queued.BranchName));
        _domainEventDispatcher.DispatchedEvents.OfType<IssueRevisionBlocked>().ShouldHaveSingleItem();
    }

    [Theory]
    [MemberData(nameof(RevisionPaths))]
    public async Task WhenAQueuedRevisionHasNoOpenBlocker_ItIsSelectedAfterReadingBlockers(string path)
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        RevisionQueuedIssue queued = QueueRevision(path, repositoryId);
        Seed(queued);
        StubIssueBlockerQueries blockerQueries = new();
        DispatchCandidateSelector sut = BuildSelector(issueBlockerQueries: blockerQueries);

        // Act
        SelectionOutcome outcome = await sut.SelectAsync(CancellationToken.None);

        // Assert
        outcome.ShouldBeOfType<SelectionOutcome.Selected>().Candidate.Issue.Id.ShouldBe(queued.Id);
        blockerQueries.RequestedIssueNumbers.ShouldBe([IssueNumber]);
    }

    [Fact]
    public async Task WhenAContinuationRetriedFromContinuableFailedHasAnOpenBlocker_ItIsHeldAndNothingIsSelected()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        Seed(Builder(repositoryId).ContinuableFailed().Retry(Now));
        StubIssueBlockerQueries blockerQueries = new StubIssueBlockerQueries().WithBlockers(IssueNumber, Blocker);
        DispatchCandidateSelector sut = BuildSelector(issueBlockerQueries: blockerQueries);

        // Act
        SelectionOutcome outcome = await sut.SelectAsync(CancellationToken.None);

        // Assert
        outcome.ShouldBeOfType<SelectionOutcome.AllCandidatesSkipped>().Blocked.ShouldBe(1);
        ContinuationBlockedIssue held = (await ReloadAsync()).ShouldBeOfType<ContinuationBlockedIssue>();
        held.ShouldSatisfyAllConditions(
            () => held.BlockedBy.ShouldBe([Blocker]),
            () => held.BranchName.ShouldBe("feat/7-held"));
        _domainEventDispatcher.DispatchedEvents.OfType<IssueContinuationBlocked>().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task WhenAFreshClaimAdoptsItsBranchAndABlockerAppears_TheContinuationIsHeldOnTheNextPass()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        FreshQueuedIssue fresh = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(IssueNumber)
            .FreshQueued();
        Seed(fresh);
        string branchName = fresh.GeneratedBranchName.Value;
        StubIssueBlockerQueries blockerQueries = new();
        StubBranchWorkQueries branchQueries = new StubBranchWorkQueries().WithCommitsAhead(branchName, 1);
        DispatchCandidateSelector sut = BuildSelector(issueBlockerQueries: blockerQueries, branchWorkQueries: branchQueries);
        await sut.SelectAsync(CancellationToken.None);
        _dbContext.ChangeTracker.Clear();
        blockerQueries.WithBlockers(IssueNumber, Blocker);

        // Act
        SelectionOutcome outcome = await sut.SelectAsync(CancellationToken.None);

        // Assert
        outcome.ShouldBeOfType<SelectionOutcome.AllCandidatesSkipped>().Blocked.ShouldBe(1);
        ContinuationBlockedIssue held = (await ReloadAsync()).ShouldBeOfType<ContinuationBlockedIssue>();
        held.ShouldSatisfyAllConditions(
            () => held.BlockedBy.ShouldBe([Blocker]),
            () => held.BranchName.ShouldBe(branchName));
    }

    [Fact]
    public async Task WhenAHeldRevisionsBlockersClose_ItIsSelectedWithItsTrigger()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        RevisionBlockedIssue held = Builder(repositoryId).RevisionBlocked([Blocker]);
        Seed(held.Unblock(Now));
        DispatchCandidateSelector sut = BuildSelector();

        // Act
        SelectionOutcome outcome = await sut.SelectAsync(CancellationToken.None);

        // Assert
        outcome.ShouldBeOfType<SelectionOutcome.Selected>().Candidate.Issue.Context
            .ShouldBeOfType<DispatchContext.Revision>().Trigger.Discriminator.ShouldBe(held.Trigger.Discriminator);
    }

    [Fact]
    public async Task WhenAQueuedReviewHasAnOpenBlocker_ItIsSelectedWithoutReadingBlockers()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        ReviewQueuedIssue queued = Builder(repositoryId).ReviewQueued();
        Seed(queued);
        StubIssueBlockerQueries blockerQueries = new StubIssueBlockerQueries().WithBlockers(IssueNumber, Blocker);
        DispatchCandidateSelector sut = BuildSelector(issueBlockerQueries: blockerQueries);

        // Act
        SelectionOutcome outcome = await sut.SelectAsync(CancellationToken.None);

        // Assert
        outcome.ShouldBeOfType<SelectionOutcome.Selected>().Candidate.Issue.Id.ShouldBe(queued.Id);
        blockerQueries.RequestedIssueNumbers.ShouldBeEmpty();
    }

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
        public Task<WorkerNuGetConfig?> GetWorkerNuGetConfigAsync(
            MonitoredRepositoryId repositoryId,
            string runSourceToken,
            CancellationToken cancellationToken) =>
            Task.FromResult<WorkerNuGetConfig?>(null);

        public Task<RepositoryDispatchInfo?> GetDispatchInfoAsync(
            MonitoredRepositoryId repositoryId,
            CancellationToken cancellationToken)
            => Task.FromResult(info);
    }
}
