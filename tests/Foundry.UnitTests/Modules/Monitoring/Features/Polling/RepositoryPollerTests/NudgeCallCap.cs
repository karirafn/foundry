using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Eligibility;
using Foundry.Modules.Monitoring.Features.Polling;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Modules.Monitoring.Features.Providers.Checks;
using Foundry.Modules.Monitoring.Features.Providers.Comments;
using Foundry.Modules.Monitoring.Features.Webhooks;
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
/// Nudge-cap invariance (ADR 0085): N webhook deliveries for one nudge key within one poll interval
/// produce at most two provider runs of that pass — the leading run and one coalesced trailing run —
/// so webhook mode never exceeds the call rate of normal polling. Drives the real
/// <see cref="NudgeScheduler"/> on a simulated clock and runs the real <see cref="RepositoryPoller"/>
/// per-pass entry point against a <see cref="CountingIssueProvider"/>.
/// </summary>
public sealed class NudgeCallCap : IAsyncDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);
    private const int IssueNumber = 7;

    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;

    public NudgeCallCap()
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

    [Theory]
    [InlineData(5)]
    [InlineData(50)]
    public async Task WhenNDeliveriesForListingKeyWithinOneInterval_AtMostTwoListingRuns(int deliveries)
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        CountingIssueProvider provider = new([]);
        RepositoryPoller poller = BuildPoller(new FixedIssueQueries([]));
        NudgeKey key = new(repository.Id, NudgePass.IssueListing);

        // Act
        await SimulateAsync(key, deliveries, async now =>
            await poller.RunListingPassAsync(repository, provider, now, CancellationToken.None));

        // Assert — leading run + one trailing run, never one per delivery.
        provider.GetIssuesCallCount.ShouldBe(2);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(50)]
    public async Task WhenNDeliveriesForChecksKeyWithinOneInterval_AtMostTwoChecksRuns(int deliveries)
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        CountingIssueProvider provider = new([]);
        ChecksPendingIssueInfo checksPending = new(
            IssueNumber,
            "https://github.com/owner/repo/pull/8",
            T0,
            T0);
        RepositoryPoller poller = BuildPoller(new FixedIssueQueries([checksPending]));
        NudgeKey key = new(repository.Id, NudgePass.Checks, IssueNumber);

        // Act
        await SimulateAsync(key, deliveries, async now =>
            await poller.RunChecksPassAsync(repository, provider, IssueNumber, now, CancellationToken.None));

        // Assert
        provider.IsIssueClosedCallCount.ShouldBeLessThanOrEqualTo(2);
        provider.IsIssueClosedCallCount.ShouldBe(2);
    }

    /// <summary>
    /// Spreads <paramref name="deliveries"/> nudges for <paramref name="key"/> evenly across one poll
    /// interval, then advances the clock second by second for two intervals, running the pass for every
    /// key the scheduler releases.
    /// </summary>
    private static async Task SimulateAsync(NudgeKey key, int deliveries, Func<DateTimeOffset, Task> runPass)
    {
        NudgeScheduler scheduler = new();
        TimeSpan spacing = (PollInterval - TimeSpan.FromSeconds(1)) / deliveries;
        List<DateTimeOffset> arrivals = Enumerable.Range(0, deliveries).Select(i => T0 + (spacing * i)).ToList();

        for (DateTimeOffset now = T0; now <= T0 + (PollInterval * 2); now += TimeSpan.FromSeconds(1))
        {
            foreach (DateTimeOffset arrival in arrivals.Where(a => a <= now).ToList())
            {
                scheduler.Add(new Nudge(key), arrival);
                arrivals.Remove(arrival);
            }

            foreach (NudgeKey _ in scheduler.TakeDue(now, _ => PollInterval))
            {
                await runPass(now);
            }
        }
    }

    private MonitoredRepository SeedRepository()
    {
        RepositorySlug slug = RepositorySlug.Create("owner/repo").ValueOrThrow();
        MonitoredRepository repository = MonitoredRepository.Create(slug, "github.com", PollInterval).ValueOrThrow();
        _dbContext.Set<MonitoredRepository>().Add(repository);
        _dbContext.SaveChanges();
        return repository;
    }

    private RepositoryPoller BuildPoller(IIssueQueries issueQueries) =>
        new(
            issueQueries,
            _dbContext,
            new NullDomainEventDispatcher(),
            new NullIntegrationEventDispatcher(),
            new NeverCalledEligibilityEvaluator(),
            new WebhookNudgeQueue(NullLogger<WebhookNudgeQueue>.Instance),
            new PostedCommentRegistry(_dbContext),
            NullLogger<RepositoryPoller>.Instance);

    /// <summary>Targeted listing and checks passes never evaluate eligibility.</summary>
    private sealed class NeverCalledEligibilityEvaluator : IRepositoryEligibilityEvaluator
    {
        public Task EvaluateFullyAndStoreAsync(MonitoredRepository repository, DateTimeOffset now, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Targeted passes must not evaluate eligibility.");

        public Task EvaluateBranchRulesAndStoreAsync(MonitoredRepository repository, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Targeted passes must not evaluate eligibility.");
    }

    private sealed class NullIntegrationEventDispatcher : IIntegrationEventDispatcher
    {
        public Task DispatchAsync(IEnumerable<IIntegrationEvent> events, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class FixedIssueQueries(IReadOnlyList<ChecksPendingIssueInfo> checksPending) : IIssueQueries
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

        public Task<IReadOnlyList<RebasePendingIssueInfo>> GetRebasePendingIssuesAsync(
            MonitoredRepositoryId repositoryId,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<RebasePendingIssueInfo>>([]);

        public Task<IReadOnlyList<ChecksPendingIssueInfo>> GetChecksPendingIssuesAsync(
            MonitoredRepositoryId repositoryId,
            CancellationToken cancellationToken)
            => Task.FromResult(checksPending);

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
