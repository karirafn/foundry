using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Eligibility;
using Foundry.Modules.Monitoring.Features.Polling;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.Polling.RepositoryPollerTests;

/// <summary>
/// Invariance tests asserting that the number of <see cref="IIssueProvider"/> calls made by
/// <see cref="RepositoryPoller.PollAsync"/> does NOT scale with issue count (AC1),
/// and does NOT exceed the declared fixed cap (AC2).
/// </summary>
public sealed class PollCallInvariance : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;

    private static readonly DateTimeOffset Now = new(2026, 5, 27, 12, 0, 0, TimeSpan.Zero);

    public PollCallInvariance()
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

    /// <summary>
    /// Seeds a repository with the Granted write-probe verdict so PollAsync takes the cheap
    /// eligibility path (branch-rules only, no write probe). This makes the provider call count
    /// deterministic and independent of eligibility state.
    /// </summary>
    private MonitoredRepository SeedRepositoryWithGrantedVerdict(string slug = "owner/repo", int position = 0)
    {
        RepositorySlug repoSlug = RepositorySlug.Create(slug).ValueOrThrow();
        MonitoredRepository repository = MonitoredRepository.Create(repoSlug, "github.com", null, position);
        repository.SetWriteProbeVerdict(new WriteProbeVerdict.Granted());
        _dbContext.Set<MonitoredRepository>().Add(repository);
        _dbContext.SaveChanges();
        return repository;
    }

    private RepositoryPoller BuildPoller(IIssueQueries issueQueries)
    {
        return new RepositoryPoller(
            issueQueries,
            _dbContext,
            new NullDomainEventDispatcher(),
            new NullIntegrationEventDispatcher(),
            new GrantedEligibilityEvaluator(),
            NullLogger<RepositoryPoller>.Instance);
    }

    /// <summary>
    /// Builds a list of N non-triggering provider issues — all brand new (not in known numbers),
    /// but since dispatch candidates are empty they produce zero dependency calls.
    /// </summary>
    private static IReadOnlyList<ProviderIssue> BuildIssues(int count)
    {
        return Enumerable.Range(1, count)
            .Select(n => new ProviderIssue(
                Number: n,
                Title: $"Issue {n}",
                Body: "Body",
                Author: "user",
                Url: $"https://github.com/owner/repo/issues/{n}",
                Labels: [],
                IssueKindLabel: "feature"))
            .ToList();
    }

    // AC1: poll-call count does not scale with total issue count.
    [Fact]
    public async Task WhenIssueCounts5And200_TotalProviderCallsAreEqual()
    {
        // Arrange
        // Empty dispatch candidates and review issues so only GetIssuesAsync is called.
        IIssueQueries issueQueries = new EmptyIssueQueries();

        MonitoredRepository repo5 = SeedRepositoryWithGrantedVerdict("owner/repo-a", position: 0);
        CountingIssueProvider provider5 = new(BuildIssues(5));
        RepositoryPoller poller5 = BuildPoller(issueQueries);

        MonitoredRepository repo200 = SeedRepositoryWithGrantedVerdict("owner/repo-b", position: 1);
        CountingIssueProvider provider200 = new(BuildIssues(200));
        RepositoryPoller poller200 = BuildPoller(issueQueries);

        // Act
        await poller5.PollAsync(repo5, provider5, Now, CancellationToken.None);
        await poller200.PollAsync(repo200, provider200, Now, CancellationToken.None);

        // Assert
        provider5.TotalCalls.ShouldBe(provider200.TotalCalls);
    }

    // AC2: fixed-cost scenario stays within the declared cap.
    [Fact]
    public async Task WhenGrantedVerdictAndNoWorkItems_TotalProviderCallsAtMostMaxFixedPollCallsPerCycle()
    {
        // Arrange
        // Granted verdict → cheap eligibility path (no write probe).
        // Empty dispatch candidates → no GetDependenciesAsync calls.
        // Empty review issues → no IsIssueClosedAsync / GetPullRequestStatusAsync / GetReviewFeedbackAsync calls.
        MonitoredRepository repository = SeedRepositoryWithGrantedVerdict();
        CountingIssueProvider provider = new();
        RepositoryPoller poller = BuildPoller(new EmptyIssueQueries());

        // Act
        await poller.PollAsync(repository, provider, Now, CancellationToken.None);

        // Assert
        provider.TotalCalls.ShouldBeLessThanOrEqualTo(RepositoryPoller.MaxFixedPollCallsPerCycle);
    }

    // AC2 zero-issues case: the invariant holds with zero issues (no division by issue count).
    [Fact]
    public async Task WhenZeroIssues_TotalProviderCallsAtMostMaxFixedPollCallsPerCycle()
    {
        // Arrange
        MonitoredRepository repository = SeedRepositoryWithGrantedVerdict();
        CountingIssueProvider provider = new([]);
        RepositoryPoller poller = BuildPoller(new EmptyIssueQueries());

        // Act
        await poller.PollAsync(repository, provider, Now, CancellationToken.None);

        // Assert
        provider.TotalCalls.ShouldBeLessThanOrEqualTo(RepositoryPoller.MaxFixedPollCallsPerCycle);
    }

    /// <summary>
    /// Eligibility evaluator that always records a Granted verdict on the cheap path.
    /// Guarantees the fixed-cost path is exercised (no write probe).
    /// </summary>
    private sealed class GrantedEligibilityEvaluator : IRepositoryEligibilityEvaluator
    {
        public Task EvaluateFullyAndStoreAsync(
            MonitoredRepository repo,
            DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            repo.SetEligibility(new RepositoryEligibility.Eligible());
            return Task.CompletedTask;
        }

        public Task EvaluateBranchRulesAndStoreAsync(
            MonitoredRepository repo,
            CancellationToken cancellationToken)
        {
            repo.SetEligibility(new RepositoryEligibility.Eligible());
            return Task.CompletedTask;
        }
    }

    private sealed class NullDomainEventDispatcher : IDomainEventDispatcher
    {
        public Task DispatchAsync(IEnumerable<IDomainEvent> events, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class NullIntegrationEventDispatcher : IIntegrationEventDispatcher
    {
        public Task DispatchAsync(IEnumerable<IIntegrationEvent> events, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    /// <summary>
    /// Returns empty results for all queries — ensures the poll sees no dispatch candidates
    /// and no review issues, so the fixed-cost path is deterministic.
    /// </summary>
    private sealed class EmptyIssueQueries : IIssueQueries
    {
        public Task<IReadOnlySet<int>> GetKnownIssueNumbersAsync(
            MonitoredRepositoryId repositoryId,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlySet<int>>(new HashSet<int>());

        public Task<IReadOnlyDictionary<int, IssueSnapshot>> GetIssueSnapshotsAsync(
            MonitoredRepositoryId repositoryId,
            IReadOnlySet<int> issueNumbers,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyDictionary<int, IssueSnapshot>>(
                new Dictionary<int, IssueSnapshot>());

        public Task<IReadOnlyList<DependencyEdge>> GetDependencyGraphAsync(
            MonitoredRepositoryId repositoryId,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<DependencyEdge>>([]);

        public Task<IReadOnlyList<ReviewIssueInfo>> GetReviewIssuesAsync(
            MonitoredRepositoryId repositoryId,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<ReviewIssueInfo>>([]);

        public Task<IReadOnlySet<int>> GetUntrackableIssueNumbersAsync(
            MonitoredRepositoryId repositoryId,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlySet<int>>(new HashSet<int>());

        public Task<IReadOnlySet<int>> GetDispatchCandidateIssueNumbersAsync(
            MonitoredRepositoryId repositoryId,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlySet<int>>(new HashSet<int>());

        public Task<IReadOnlyList<IssueSummary>> GetIssueSummariesAsync(
            MonitoredRepositoryId? repositoryId,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<IssueSummary>>([]);

        public Task<IssueSummary?> GetIssueSummaryAsync(
            IssueId issueId,
            CancellationToken cancellationToken)
            => Task.FromResult<IssueSummary?>(null);

        public Task<Result<IssueDetail>> GetIssueDetailAsync(
            IssueId issueId,
            CancellationToken cancellationToken)
            => Task.FromResult(Result<IssueDetail>.Fail(IssueErrors.NotFound(issueId)));

        public Task<IReadOnlyList<IssueSummary>> GetActiveIssueSummariesAsync(
            MonitoredRepositoryId? repositoryId,
            IReadOnlyCollection<string>? states,
            CancellationToken cancellationToken)
            => throw new NotImplementedException();

        public Task<PagedIssues> GetResolvedIssueSummariesAsync(
            MonitoredRepositoryId? repositoryId,
            IReadOnlyCollection<string> states,
            string? cursor,
            int limit,
            CancellationToken cancellationToken)
            => throw new NotImplementedException();

        public Task<IssueStateCounts> GetIssueStateCountsAsync(
            MonitoredRepositoryId? repositoryId,
            CancellationToken cancellationToken)
            => throw new NotImplementedException();
    }
}
