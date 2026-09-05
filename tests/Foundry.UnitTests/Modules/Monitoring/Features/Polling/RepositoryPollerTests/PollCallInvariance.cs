using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.CredentialResolution;
using Foundry.Modules.Monitoring.Features.Eligibility;
using Foundry.Modules.Monitoring.Features.Polling;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Modules.Monitoring.Infrastructure.GitHub;
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
///
/// The real <see cref="RepositoryEligibilityEvaluator"/> is wired with a stub
/// <see cref="ICredentialResolver"/> and a <see cref="FixedCountingProviderFactory"/> that
/// always returns the same <see cref="CountingIssueProvider"/> instance. This ensures the
/// eligibility branch-rules GET (<c>GetBranchProtectionAsync</c>) is counted in
/// <c>TotalCalls</c>, so the cap has zero slack: adding one unconditional provider call
/// to <c>PollAsync</c> pushes <c>TotalCalls</c> from 2 to 3 and breaks AC2.
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
    /// Seeds a repository with the Granted write-probe verdict so <c>PollAsync</c> takes the
    /// cheap eligibility path (branch-rules only, no write probe). The Granted verdict makes
    /// <c>IsDueForWriteProbe</c> return false, so <c>EvaluateBranchRulesAndStoreAsync</c>
    /// is called — exactly one <c>GetBranchProtectionAsync</c> provider call.
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

    /// <summary>
    /// Builds a <see cref="RepositoryPoller"/> wired with the real
    /// <see cref="RepositoryEligibilityEvaluator"/> backed by the given
    /// <paramref name="countingProvider"/>. The factory always returns the same
    /// counting provider instance, so every call the evaluator or poll cycle makes
    /// is counted in <c>TotalCalls</c>.
    /// </summary>
    private RepositoryPoller BuildPoller(CountingIssueProvider countingProvider, IIssueQueries issueQueries)
    {
        GitHubCredential credential = GitHubCredential.Create(
            "test",
            "token",
            BaseUrl.Create("https://github.com").ValueOrThrow());

        IRepositoryEligibilityEvaluator eligibilityEvaluator = new RepositoryEligibilityEvaluator(
            new StubCredentialResolver(credential),
            new FixedCountingProviderFactory(countingProvider),
            new NeverCalledWriteProber(),
            NullLogger<RepositoryEligibilityEvaluator>.Instance);

        return new RepositoryPoller(
            issueQueries,
            _dbContext,
            new NullDomainEventDispatcher(),
            new NullIntegrationEventDispatcher(),
            eligibilityEvaluator,
            NullLogger<RepositoryPoller>.Instance);
    }

    /// <summary>
    /// Builds a list of N non-triggering provider issues. All are brand new (not in known
    /// numbers), but since dispatch candidates are empty they produce zero dependency calls.
    /// </summary>
    private static IReadOnlyList<ProviderIssue> BuildIssues(int count)
    {
        return Enumerable.Range(1, count)
            .Select(n => new ProviderIssue(
                Number: n,
                Title: $"Issue {n}",
                Author: "user",
                Url: $"https://github.com/owner/repo/issues/{n}",
                Labels: [],
                IssueKindLabel: "feature"))
            .ToList();
    }

    // AC1: poll-call count does not scale with total issue count.
    // Both 5-issue and 200-issue polls issue exactly MaxFixedPollCallsPerCycle (2) provider calls:
    //   1. GetBranchProtectionAsync (eligibility branch-rules GET via real evaluator)
    //   2. GetIssuesAsync (listing)
    [Fact]
    public async Task WhenIssueCounts5And200_TotalProviderCallsAreEqual()
    {
        // Arrange
        IIssueQueries issueQueries = new EmptyIssueQueries();

        MonitoredRepository repo5 = SeedRepositoryWithGrantedVerdict("owner/repo-a", position: 0);
        CountingIssueProvider provider5 = new(BuildIssues(5));
        RepositoryPoller poller5 = BuildPoller(provider5, issueQueries);

        MonitoredRepository repo200 = SeedRepositoryWithGrantedVerdict("owner/repo-b", position: 1);
        CountingIssueProvider provider200 = new(BuildIssues(200));
        RepositoryPoller poller200 = BuildPoller(provider200, issueQueries);

        // Act
        await poller5.PollAsync(repo5, provider5, Now, CancellationToken.None);
        await poller200.PollAsync(repo200, provider200, Now, CancellationToken.None);

        // Assert
        provider5.TotalCalls.ShouldBe(provider200.TotalCalls);
    }

    // AC2: fixed-cost scenario equals the declared cap — zero slack.
    // TotalCalls must be exactly MaxFixedPollCallsPerCycle (2):
    //   1. GetBranchProtectionAsync (real evaluator → FixedCountingProviderFactory)
    //   2. GetIssuesAsync (listing)
    // Adding any one unconditional provider call to PollAsync pushes TotalCalls to 3, failing this test.
    [Fact]
    public async Task WhenGrantedVerdictAndNoWorkItems_TotalProviderCallsEqualMaxFixedPollCallsPerCycle()
    {
        // Arrange
        // Granted verdict → cheap eligibility path → 1 GetBranchProtectionAsync.
        // Empty dispatch candidates → no GetDependenciesAsync.
        // Empty review issues → no IsIssueClosedAsync / GetPullRequestStatusAsync / GetReviewFeedbackAsync.
        MonitoredRepository repository = SeedRepositoryWithGrantedVerdict();
        CountingIssueProvider provider = new();
        RepositoryPoller poller = BuildPoller(provider, new EmptyIssueQueries());

        // Act
        await poller.PollAsync(repository, provider, Now, CancellationToken.None);

        // Assert — equality, not <=, so adding any unconditional provider call breaks this test.
        provider.TotalCalls.ShouldBe(RepositoryPoller.MaxFixedPollCallsPerCycle);
    }

    // AC2 zero-issues edge case: invariant holds when the listing is empty.
    [Fact]
    public async Task WhenZeroIssues_TotalProviderCallsEqualMaxFixedPollCallsPerCycle()
    {
        // Arrange
        MonitoredRepository repository = SeedRepositoryWithGrantedVerdict();
        CountingIssueProvider provider = new([]);
        RepositoryPoller poller = BuildPoller(provider, new EmptyIssueQueries());

        // Act
        await poller.PollAsync(repository, provider, Now, CancellationToken.None);

        // Assert
        provider.TotalCalls.ShouldBe(RepositoryPoller.MaxFixedPollCallsPerCycle);
    }

    /// <summary>
    /// Returns the configured credential for any host/slug, simulating a repo that has credentials.
    /// </summary>
    private sealed class StubCredentialResolver(Credential credential) : ICredentialResolver
    {
        public Task<Credential?> ResolveAsync(
            string host,
            RepositorySlug slug,
            CancellationToken cancellationToken)
            => Task.FromResult<Credential?>(credential);
    }

    /// <summary>
    /// Always returns the same <see cref="CountingIssueProvider"/> regardless of credential,
    /// so every provider call — from the eligibility evaluator and from the poll cycle — is counted.
    /// </summary>
    private sealed class FixedCountingProviderFactory(CountingIssueProvider provider) : IIssueProviderFactory
    {
        public IIssueProvider CreateProvider(Credential credential, string token) => provider;
    }

    /// <summary>
    /// Write prober that throws if called — the cheap eligibility path (Granted verdict) must
    /// never invoke the write probe. A call here signals a broken test setup.
    /// </summary>
    private sealed class NeverCalledWriteProber : IGitHubWriteProber
    {
        public Task<Result<WritePermissionProbeResult>> ProbeWriteAccessAsync(
            Uri apiBaseUrl,
            RepositorySlug slug,
            string token,
            CancellationToken cancellationToken)
            => throw new InvalidOperationException(
                "Write prober must not be called on the cheap eligibility path (Granted verdict).");
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
    /// and no review issues, so the fixed-cost path is fully deterministic.
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
