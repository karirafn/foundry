using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.CredentialResolution;
using Foundry.Modules.Monitoring.Features.Eligibility;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Modules.Monitoring.Features.Providers.Feedback;
using Foundry.Modules.Monitoring.Features.Polling;
using Foundry.Modules.Settings.Contracts.Queries;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.Polling.MonitoringServiceTests;

public sealed class ExecuteTickAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;

    private static readonly DateTimeOffset Now = new(2026, 5, 27, 12, 0, 0, TimeSpan.Zero);

    private static RepositorySlug ValidSlug(string slug) =>
        RepositorySlug.Create(slug).ValueOrThrow();

    public ExecuteTickAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        // Initialize the schema via a transient context
        using FoundryDbContext setup = CreateDbContext();
        setup.Database.EnsureCreated();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _connection.DisposeAsync();
    }

    private FoundryDbContext CreateDbContext()
    {
        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .Options;
        return new FoundryDbContext(options);
    }

    private static MonitoringService BuildService(ServiceProvider serviceProvider)
    {
        IServiceScopeFactory scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();

        return new MonitoringService(
            scopeFactory,
            NullLogger<MonitoringService>.Instance);
    }

    private ServiceProvider BuildServiceProvider(
        IIssueProviderFactory providerFactory,
        IGlobalSettingsQueries? settingsQueries = null)
    {
        SqliteConnection connection = _connection;

        ServiceCollection services = new();

        // Register as scoped so that MonitoringService and RepositoryPoller share the same
        // DbContext instance within a single tick scope — required for entity change tracking.
        services.AddScoped<FoundryDbContext>(_ =>
        {
            DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
                .UseSqlite(connection)
                .Options;
            return new FoundryDbContext(options);
        });
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<FoundryDbContext>());

        services.AddLogging();
        services.AddScoped<IIssueQueries, StubIssueQueries>();
        services.AddScoped<IDomainEventDispatcher, NullDomainEventDispatcher>();
        services.AddScoped<IIntegrationEventDispatcher, NullIntegrationEventDispatcher>();
        services.AddScoped<IRepositoryEligibilityEvaluator, NullRepositoryEligibilityEvaluator>();
        services.AddScoped<IIssueProviderFactory>(_ => providerFactory);
        services.AddScoped<ICredentialResolver, CredentialResolver>();
        services.AddScoped<RepositoryPoller>();

        // Default: poll interval of 30 seconds (mirrors GlobalSettings default).
        IGlobalSettingsQueries queries = settingsQueries ?? new StubSettingsQueries(30);
        services.AddScoped<IGlobalSettingsQueries>(_ => queries);

        return services.BuildServiceProvider();
    }

    private async Task<MonitoredRepositoryId> SeedActiveRepoAsync(string slug = "owner/repo", string? token = "ghp_test_token")
    {
        await using FoundryDbContext db = CreateDbContext();

        RepositorySlug repoSlug = ValidSlug(slug);
        GitHubCredential account = GitHubCredential.Create("my-org", token, BaseUrl.Create("https://github.com").ValueOrThrow());
        account.SetNamespaces([Namespace.Create(repoSlug.Owner).ValueOrThrow()]);
        db.Set<Credential>().Add(account);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        MonitoredRepository repo = MonitoredRepository.Create(repoSlug, "github.com", null);
        db.Set<MonitoredRepository>().Add(repo);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        return repo.Id;
    }

    [Fact]
    public async Task WhenActiveRepoDueForPoll_UpdatesLastPolledAt()
    {
        // Arrange
        MonitoredRepositoryId repoId = await SeedActiveRepoAsync();

        using ServiceProvider sp = BuildServiceProvider(new EmptyIssueProviderFactory());
        MonitoringService sut = BuildService(sp);

        // Act
        await sut.ExecuteTickAsync(Now, TestContext.Current.CancellationToken);

        // Assert — read from a fresh context after the scope has completed
        await using FoundryDbContext assertDb = CreateDbContext();
        MonitoredRepository? repo = await assertDb.Set<MonitoredRepository>()
            .FindAsync([repoId], TestContext.Current.CancellationToken);
        repo.ShouldNotBeNull();
        repo.LastPolledAt.ShouldBe(Now);
    }

    [Fact]
    public async Task WhenAccountHasNoToken_RepoLastPolledAtIsNotUpdated()
    {
        // Arrange
        MonitoredRepositoryId repoId = await SeedActiveRepoAsync(token: null);

        using ServiceProvider sp = BuildServiceProvider(new EmptyIssueProviderFactory());
        MonitoringService sut = BuildService(sp);

        // Act
        await sut.ExecuteTickAsync(Now, TestContext.Current.CancellationToken);

        // Assert — poll was skipped, LastPolledAt not updated
        await using FoundryDbContext assertDb = CreateDbContext();
        MonitoredRepository? repo = await assertDb.Set<MonitoredRepository>()
            .FindAsync([repoId], TestContext.Current.CancellationToken);
        repo.ShouldNotBeNull();
        repo.LastPolledAt.ShouldBeNull();
    }

    [Fact]
    public async Task WhenRepoNotDueForPoll_LastPolledAtNotUpdatedAgain()
    {
        // Arrange
        MonitoredRepositoryId repoId = await SeedActiveRepoAsync();

        // Mark as just polled — not due for 30s interval
        await using (FoundryDbContext db = CreateDbContext())
        {
            MonitoredRepository? repo = await db.Set<MonitoredRepository>()
                .FindAsync([repoId], TestContext.Current.CancellationToken);
            repo.ShouldNotBeNull();
            repo.MarkPolled(Now);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using ServiceProvider sp = BuildServiceProvider(new EmptyIssueProviderFactory());
        MonitoringService sut = BuildService(sp);

        // Act — tick 1 second later
        await sut.ExecuteTickAsync(Now.AddSeconds(1), TestContext.Current.CancellationToken);

        // Assert — LastPolledAt stays as originally set
        await using FoundryDbContext assertDb = CreateDbContext();
        MonitoredRepository? assertRepo = await assertDb.Set<MonitoredRepository>()
            .FindAsync([repoId], TestContext.Current.CancellationToken);
        assertRepo.ShouldNotBeNull();
        assertRepo.LastPolledAt.ShouldBe(Now);
    }

    [Fact]
    public async Task WhenNoActiveRepos_CompletesWithoutError()
    {
        // Arrange — no repos seeded
        using ServiceProvider sp = BuildServiceProvider(new EmptyIssueProviderFactory());
        MonitoringService sut = BuildService(sp);

        // Act
        Exception? exception = await Record.ExceptionAsync(
            () => sut.ExecuteTickAsync(Now, TestContext.Current.CancellationToken));

        // Assert
        exception.ShouldBeNull();
    }

    [Fact]
    public async Task WhenPollIntervalQueriedPerTick_DueNessReflectsQueryValue()
    {
        // Arrange — seed a repo, mark it polled at Now
        MonitoredRepositoryId repoId = await SeedActiveRepoAsync();

        await using (FoundryDbContext db = CreateDbContext())
        {
            MonitoredRepository? repo = await db.Set<MonitoredRepository>()
                .FindAsync([repoId], TestContext.Current.CancellationToken);
            repo.ShouldNotBeNull();
            repo.MarkPolled(Now);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        // Act — tick 5 seconds later with a 3-second interval returned by queries
        // (interval < elapsed time, so due for poll)
        StubSettingsQueries queriesReturningShortInterval = new(pollIntervalSeconds: 3);
        using ServiceProvider sp = BuildServiceProvider(
            new EmptyIssueProviderFactory(),
            settingsQueries: queriesReturningShortInterval);
        MonitoringService sut = BuildService(sp);
        await sut.ExecuteTickAsync(Now.AddSeconds(5), TestContext.Current.CancellationToken);

        // Assert — repo was polled because the per-tick interval (3s) elapsed
        await using FoundryDbContext assertDb = CreateDbContext();
        MonitoredRepository? assertRepo = await assertDb.Set<MonitoredRepository>()
            .FindAsync([repoId], TestContext.Current.CancellationToken);
        assertRepo.ShouldNotBeNull();
        assertRepo.LastPolledAt.ShouldBe(Now.AddSeconds(5));
    }

    [Fact]
    public async Task WhenPollIntervalQueriedPerTick_LongIntervalPreventsEarlyPoll()
    {
        // Arrange — seed a repo, mark it polled at Now
        MonitoredRepositoryId repoId = await SeedActiveRepoAsync();

        await using (FoundryDbContext db = CreateDbContext())
        {
            MonitoredRepository? repo = await db.Set<MonitoredRepository>()
                .FindAsync([repoId], TestContext.Current.CancellationToken);
            repo.ShouldNotBeNull();
            repo.MarkPolled(Now);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        // Act — tick 5 seconds later with a 60-second interval (not due)
        StubSettingsQueries queriesReturningLongInterval = new(pollIntervalSeconds: 60);
        using ServiceProvider sp = BuildServiceProvider(
            new EmptyIssueProviderFactory(),
            settingsQueries: queriesReturningLongInterval);
        MonitoringService sut = BuildService(sp);
        await sut.ExecuteTickAsync(Now.AddSeconds(5), TestContext.Current.CancellationToken);

        // Assert — repo was NOT polled because interval (60s) has not elapsed
        await using FoundryDbContext assertDb = CreateDbContext();
        MonitoredRepository? assertRepo = await assertDb.Set<MonitoredRepository>()
            .FindAsync([repoId], TestContext.Current.CancellationToken);
        assertRepo.ShouldNotBeNull();
        assertRepo.LastPolledAt.ShouldBe(Now);
    }

    private sealed class StubSettingsQueries(int pollIntervalSeconds) : IGlobalSettingsQueries
    {
        public Task<int> GetPollIntervalSecondsAsync(CancellationToken cancellationToken)
            => Task.FromResult(pollIntervalSeconds);

        public Task<Foundry.Modules.Settings.Contracts.GlobalSettingsSummary?> GetSettingsAsync(
            CancellationToken cancellationToken)
            => Task.FromResult<Foundry.Modules.Settings.Contracts.GlobalSettingsSummary?>(null);

        public Task<int> GetMaxConcurrentAsync(CancellationToken cancellationToken)
            => Task.FromResult(1);

        public Task<int> GetTimeoutMinutesAsync(CancellationToken cancellationToken)
            => Task.FromResult(120);

        public Task<int> GetProbeIntervalMinutesAsync(CancellationToken cancellationToken)
            => Task.FromResult(60);

        public Task<(string? SystemPromptTemplate, string? WorkerPromptTemplate)> GetPromptTemplatesAsync(
            CancellationToken cancellationToken)
            => Task.FromResult<(string?, string?)>((null, null));

        public Task<Foundry.Modules.Settings.Contracts.Queries.DispatchPauseState> GetDispatchPauseStateAsync(
            CancellationToken cancellationToken)
            => Task.FromResult(new Foundry.Modules.Settings.Contracts.Queries.DispatchPauseState(null, false, true));

        public Task<Foundry.Modules.Settings.Contracts.ImageBuildStatus> GetImageBuildStatusAsync(
            CancellationToken cancellationToken)
            => Task.FromResult(Foundry.Modules.Settings.Contracts.ImageBuildStatus.Idle);

        public Task<bool> GetWorkerImageInstallsDockerAsync(CancellationToken cancellationToken)
            => Task.FromResult(false);

        public Task<IReadOnlyDictionary<string, string>> GetWorkerImageBuildArgsAsync(
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());

        public Task<IReadOnlyList<string>> GetAllowedProviderHostsAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<string>>([]);
    }

    private sealed class EmptyIssueProviderFactory : IIssueProviderFactory
    {
        public IIssueProvider CreateProvider(Credential credential, string token)
        {
            return new EmptyIssueProvider();
        }

        private sealed class EmptyIssueProvider : IIssueProvider
        {
            public Task<Result<IssueListing>> GetIssuesAsync(
                RepositorySlug slug,
                CancellationToken cancellationToken)
            {
                return Task.FromResult(Result<IssueListing>.Ok(new IssueListing([], IsComplete: true)));
            }

            public Task<Result<IReadOnlyList<int>>> GetDependenciesAsync(
                RepositorySlug slug,
                int issueNumber,
                CancellationToken cancellationToken)
            {
                return Task.FromResult(Result<IReadOnlyList<int>>.Ok([]));
            }

            public Task<Result<bool>> IsIssueClosedAsync(
                RepositorySlug slug,
                int issueNumber,
                CancellationToken cancellationToken)
            {
                return Task.FromResult(Result<bool>.Ok(false));
            }

            public Task<Result<PullRequestStatus>> GetPullRequestStatusAsync(
                RepositorySlug slug,
                string pullRequestUrl,
                CancellationToken cancellationToken)
            {
                return Task.FromResult(
                    Result<PullRequestStatus>.Ok(new PullRequestStatus(IsClosed: false, IsMerged: false)));
            }

            public Task<Result<ReviewFeedback>> GetReviewFeedbackAsync(
                RepositorySlug slug,
                string pullRequestUrl,
                DateTimeOffset since,
                CancellationToken cancellationToken)
            {
                return Task.FromResult(Result<ReviewFeedback>.Ok(new ReviewFeedback([], OmittedCommentCount: 0, NewestCommentAt: null)));
            }

            public Task<Result<BranchProtection>> GetBranchProtectionAsync(
                RepositorySlug slug,
                CancellationToken cancellationToken)
            {
                return Task.FromResult(
                    Result<BranchProtection>.Ok(new BranchProtection("main", false, false, false)));
            }

            public Task<Result<bool>> CreateBranchAsync(
                RepositorySlug slug,
                string branchName,
                CancellationToken cancellationToken)
            {
                return Task.FromResult(Result<bool>.Ok(true));
            }

            public Task<Result<MergeRequestByBranch>> GetMergeRequestByBranchAsync(
                RepositorySlug slug,
                string branchName,
                CancellationToken cancellationToken)
            {
                return Task.FromResult(
                    Result<MergeRequestByBranch>.Ok(new MergeRequestByBranch(MergeRequestPresence.None, null)));
            }

            public Task<Result<BranchCommitSummary>> GetBranchCommitSummaryAsync(
                RepositorySlug slug,
                string branchName,
                CancellationToken cancellationToken)
        {
                return Task.FromResult(
                Result<BranchCommitSummary>.Fail(new Error("Provider.NoCommit", "No commit found")));
        }

            public Task<Result<bool>> CanPushAsync(
                RepositorySlug slug,
                CancellationToken cancellationToken)
                => Task.FromResult(Result<bool>.Ok(true));
        }
    }

    private sealed class StubIssueQueries : IIssueQueries
    {
        public Task<IReadOnlySet<int>> GetKnownIssueNumbersAsync(
            MonitoredRepositoryId repositoryId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlySet<int>>(new HashSet<int>());
        }

        public Task<IReadOnlyDictionary<int, IssueSnapshot>> GetIssueSnapshotsAsync(
            MonitoredRepositoryId repositoryId,
            IReadOnlySet<int> issueNumbers,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyDictionary<int, IssueSnapshot>>(
                new Dictionary<int, IssueSnapshot>());
        }

        public Task<IReadOnlyList<DependencyEdge>> GetDependencyGraphAsync(
            MonitoredRepositoryId repositoryId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<DependencyEdge>>([]);
        }

        public Task<IReadOnlyList<ReviewIssueInfo>> GetReviewIssuesAsync(
            MonitoredRepositoryId repositoryId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<ReviewIssueInfo>>([]);
        }

        public Task<IReadOnlyList<IssueSummary>> GetIssueSummariesAsync(
            MonitoredRepositoryId? repositoryId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<IssueSummary>>([]);
        }

        public Task<IssueSummary?> GetIssueSummaryAsync(IssueId issueId, CancellationToken cancellationToken)
        {
            return Task.FromResult<IssueSummary?>(null);
        }

        public Task<Result<IssueDetail>> GetIssueDetailAsync(IssueId issueId, CancellationToken cancellationToken)
        {
            return Task.FromResult(Result<IssueDetail>.Fail(IssueErrors.NotFound(issueId)));
        }

        public Task<IReadOnlySet<int>> GetUntrackableIssueNumbersAsync(
            MonitoredRepositoryId repositoryId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlySet<int>>(new HashSet<int>());
        }

        public Task<IReadOnlySet<int>> GetDispatchCandidateIssueNumbersAsync(
            MonitoredRepositoryId repositoryId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlySet<int>>(new HashSet<int>());
        }

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

    private sealed class NullDomainEventDispatcher : IDomainEventDispatcher
    {
        public Task DispatchAsync(IEnumerable<IDomainEvent> events, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class NullIntegrationEventDispatcher : IIntegrationEventDispatcher
    {
        public Task DispatchAsync(IEnumerable<IIntegrationEvent> events, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class NullRepositoryEligibilityEvaluator : IRepositoryEligibilityEvaluator
    {
        public Task EvaluateFullyAndStoreAsync(
            MonitoredRepository repo,
            DateTimeOffset now,
            CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task EvaluateBranchRulesAndStoreAsync(
            MonitoredRepository repo,
            CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}
