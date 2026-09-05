using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Features.Claiming;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Features.Claiming.DispatchCandidateSelectorTests;

public sealed class SelectAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;

    private static readonly RepositoryDispatchInfo DefaultDispatchInfo = new(
        "owner/repo",
        new Uri("https://github.com/owner/repo.git"),
        "GITHUB_PAT",
        new WorkerProvider.GitHub(),
        "https://api.github.com/repos/owner/repo/issues");

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
        IRepositoryDispatchQueries? repositoryDispatchQueries = null)
    {
        return new DispatchCandidateSelector(
            _dbContext,
            repositoryDispatchQueries ?? new StubRepositoryDispatchQueries(DefaultDispatchInfo),
            repositoryEligibilityQuery ?? new AllEligibleRepositoryEligibilityQuery());
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
                [new EligibleRepository(repositoryId.Value, Position: 0)]));

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

    // Cycle 4: all candidates' repos are unresolvable — returns AllCandidatesUnresolvable with count
    [Fact]
    public async Task WhenAllCandidatesUnresolvable_ReturnsAllCandidatesUnresolvableWithSkipCount()
    {
        // Arrange
        MonitoredRepositoryId repoId = MonitoredRepositoryId.New();
        SeedQueuedIssue(repoId, issueNumber: 1);
        SeedQueuedIssue(repoId, issueNumber: 2);

        DispatchCandidateSelector sut = BuildSelector(
            repositoryEligibilityQuery: new StubRepositoryEligibilityQuery(
                [new EligibleRepository(repoId.Value, Position: 0)]),
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

        // Unresolvable repo has older DetectedAt → lower DispatchOrderKey → heads the queue
        FreshQueuedIssue unresolvableIssue = SeedQueuedIssueAtTime(unresolvableRepoId, issueNumber: 1, detectedAt: olderTime);
        FreshQueuedIssue resolvableIssue = SeedQueuedIssueAtTime(resolvableRepoId, issueNumber: 2, detectedAt: newerTime);

        DispatchCandidateSelector sut = BuildSelector(
            repositoryEligibilityQuery: new StubRepositoryEligibilityQuery(
            [
                new EligibleRepository(unresolvableRepoId.Value, Position: 0),
                new EligibleRepository(resolvableRepoId.Value, Position: 0),
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
                [new EligibleRepository(unresolvableRepoId.Value, Position: 0)]),
            repositoryDispatchQueries: countingQueries);

        // Act
        await sut.SelectAsync(CancellationToken.None);

        // Assert — dispatch info for the unresolvable repo queried exactly once (memoized)
        countingQueries.CallCount(unresolvableRepoId).ShouldBe(1);
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
                .Select(id => new EligibleRepository(id, Position: 0))
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
