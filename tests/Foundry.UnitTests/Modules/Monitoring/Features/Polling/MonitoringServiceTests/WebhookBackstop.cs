using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.CredentialResolution;
using Foundry.Modules.Monitoring.Features.Eligibility;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Modules.Monitoring.Features.Providers.Checks;
using Foundry.Modules.Monitoring.Features.Providers.Feedback;
using Foundry.Modules.Monitoring.Features.Polling;
using Foundry.Modules.Monitoring.Features.Webhooks;
using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Contracts.Queries;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.Polling.MonitoringServiceTests;

/// <summary>
/// The health-gated backstop and nudge consumption of <see cref="MonitoringService"/> (ADR 0085).
/// </summary>
public sealed class WebhookBackstop : IAsyncDisposable
{
    private const int WebhookPort = 5099;
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection;

    public WebhookBackstop()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        using FoundryDbContext setup = CreateDbContext();
        setup.Database.EnsureCreated();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task WhenActiveAndIngressConfigured_PollsAtBackstopNotNormalInterval()
    {
        // Arrange
        MonitoredRepositoryId repoId = await SeedRepoAsync(active: true, lastPolledAt: Now.AddMinutes(-5));
        CountingIssueProvider provider = new([]);
        MonitoringService service = BuildService(BuildServiceProvider(provider, ingressConfigured: true));

        // Act
        await service.ExecuteTickAsync(Now, TestContext.Current.CancellationToken);

        // Assert
        provider.TotalCalls.ShouldBe(0);
        (await LoadAsync(repoId)).LastPolledAt.ShouldBe(Now.AddMinutes(-5));
    }

    [Fact]
    public async Task WhenActiveButIngressNotConfigured_PollsAtNormalInterval()
    {
        // Arrange
        MonitoredRepositoryId repoId = await SeedRepoAsync(active: true, lastPolledAt: Now.AddMinutes(-5));
        CountingIssueProvider provider = new([]);
        MonitoringService service = BuildService(BuildServiceProvider(provider, ingressConfigured: false));

        // Act
        await service.ExecuteTickAsync(Now, TestContext.Current.CancellationToken);

        // Assert
        provider.GetIssuesCallCount.ShouldBe(1);
        (await LoadAsync(repoId)).LastPolledAt.ShouldBe(Now);
    }

    [Fact]
    public async Task WhenScheduledPollOfActiveHookFindsMappedChange_BecomesSilent()
    {
        // Arrange — the listing returns an issue Foundry does not know yet: a missed "issues" delivery.
        MonitoredRepositoryId repoId = await SeedRepoAsync(active: true, lastPolledAt: Now.AddMinutes(-16));
        CountingIssueProvider provider = new([NewIssue(5)]);
        MonitoringService service = BuildService(BuildServiceProvider(provider, ingressConfigured: true));

        // Act
        await service.ExecuteTickAsync(Now, TestContext.Current.CancellationToken);

        // Assert
        MonitoredRepository repo = await LoadAsync(repoId);
        repo.WebhookStatus.ShouldBe(WebhookStatus.Silent);
        repo.IsDueForPoll(TimeSpan.FromSeconds(30), Now.AddSeconds(31), TimeSpan.FromMinutes(15)).ShouldBeTrue();
    }

    [Fact]
    public async Task WhenScheduledPollOfActiveHookFindsNoChange_StaysActive()
    {
        // Arrange
        MonitoredRepositoryId repoId = await SeedRepoAsync(active: true, lastPolledAt: Now.AddMinutes(-16));
        CountingIssueProvider provider = new([]);
        MonitoringService service = BuildService(BuildServiceProvider(provider, ingressConfigured: true));

        // Act
        await service.ExecuteTickAsync(Now, TestContext.Current.CancellationToken);

        // Assert
        provider.GetIssuesCallCount.ShouldBe(1);
        (await LoadAsync(repoId)).WebhookStatus.ShouldBe(WebhookStatus.Active);
    }

    [Fact]
    public async Task WhenListingNudgeIsDue_RunsOnlyListingPassWithoutStampingPollTime()
    {
        // Arrange
        MonitoredRepositoryId repoId = await SeedRepoAsync(active: true, lastPolledAt: Now.AddMinutes(-1));
        CountingIssueProvider provider = new([]);
        MonitoringService service = BuildService(BuildServiceProvider(provider, ingressConfigured: true));
        service.Scheduler.Add(new Nudge(new NudgeKey(repoId, NudgePass.IssueListing)), Now);

        // Act
        await service.ExecuteNudgesAsync(Now, TestContext.Current.CancellationToken);

        // Assert — one listing call, no eligibility/branch-rules call, cadence untouched.
        provider.GetIssuesCallCount.ShouldBe(1);
        provider.TotalCalls.ShouldBe(1);
        (await LoadAsync(repoId)).LastPolledAt.ShouldBe(Now.AddMinutes(-1));
    }

    [Fact]
    public async Task WhenNudgeForInactiveRepository_IsDropped()
    {
        // Arrange
        MonitoredRepositoryId repoId = await SeedRepoAsync(active: true, lastPolledAt: Now, isActive: false);
        CountingIssueProvider provider = new([]);
        MonitoringService service = BuildService(BuildServiceProvider(provider, ingressConfigured: true));
        service.Scheduler.Add(new Nudge(new NudgeKey(repoId, NudgePass.IssueListing)), Now);

        // Act
        await service.ExecuteNudgesAsync(Now, TestContext.Current.CancellationToken);

        // Assert
        provider.TotalCalls.ShouldBe(0);
        service.Scheduler.PendingCount.ShouldBe(0);
    }

    private static ProviderIssue NewIssue(int number) =>
        new(number, $"Issue {number}", "user", $"https://github.com/owner/repo/issues/{number}", ["foundry"], "feature");

    private FoundryDbContext CreateDbContext()
    {
        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .Options;
        return new FoundryDbContext(options);
    }

    private async Task<MonitoredRepositoryId> SeedRepoAsync(bool active, DateTimeOffset lastPolledAt, bool isActive = true)
    {
        await using FoundryDbContext db = CreateDbContext();
        MonitoredRepository repo = MonitoredRepository.Create(
            RepositorySlug.Create("owner/repo").ValueOrThrow(),
            "github.com",
            null).ValueOrThrow();
        repo.SetWriteProbeVerdict(new WriteProbeVerdict.Granted());

        if (active)
        {
            repo.MarkWebhookRegistered();
            repo.RecordWebhookDelivery();
        }

        if (!isActive)
        {
            repo.Update(null, isActive: false, MonitoredRepository.DefaultMaxConcurrentWorkers);
        }

        repo.MarkPolled(lastPolledAt);
        db.Set<MonitoredRepository>().Add(repo);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return repo.Id;
    }

    private async Task<MonitoredRepository> LoadAsync(MonitoredRepositoryId id)
    {
        await using FoundryDbContext db = CreateDbContext();
        return await db.Set<MonitoredRepository>().SingleAsync(r => r.Id == id, TestContext.Current.CancellationToken);
    }

    private static MonitoringService BuildService(ServiceProvider serviceProvider) =>
        new(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            new WebhookNudgeQueue(NullLogger<WebhookNudgeQueue>.Instance),
            TimeProvider.System,
            NullLogger<MonitoringService>.Instance);

    private ServiceProvider BuildServiceProvider(CountingIssueProvider provider, bool ingressConfigured)
    {
        SqliteConnection connection = _connection;
        ServiceCollection services = new();

        services.AddScoped<FoundryDbContext>(_ => new FoundryDbContext(
            new DbContextOptionsBuilder<FoundryDbContext>().UseSqlite(connection).Options));
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<FoundryDbContext>());
        services.AddLogging();
        services.AddScoped<IIssueQueries, StubIssueQueries>();
        services.AddScoped<IDomainEventDispatcher, NullDomainEventDispatcher>();
        services.AddScoped<IIntegrationEventDispatcher, NullIntegrationEventDispatcher>();
        services.AddScoped<IRepositoryEligibilityEvaluator, NullRepositoryEligibilityEvaluator>();
        services.AddScoped<IIssueProviderFactory>(_ => new FixedProviderFactory(provider));

        GitHubCredential credential = GitHubCredential.Create(
            "my-org",
            "ghp_test_token",
            BaseUrl.Create("https://github.com").ValueOrThrow());
        services.AddScoped<ICredentialResolver>(_ => new StubCredentialResolverWithFixedCredential(credential));
        services.AddScoped<RepositoryPoller>();
        services.AddScoped<IGlobalSettingsQueries>(_ => new StubSettingsQueries(30));

        services.AddSingleton(Options.Create(new WebhookIngressOptions { Port = WebhookPort }));
        services.AddScoped<IWebhookSettingsQueries>(_ => new StubWebhookSettingsQueries(
            ingressConfigured ? "https://hooks.example.com" : null));
        services.AddScoped<WebhookIngress>();

        return services.BuildServiceProvider();
    }

    private sealed class FixedProviderFactory(CountingIssueProvider provider) : IIssueProviderFactory
    {
        public IIssueProvider CreateProvider(Credential credential, string token) => provider;
    }

    private sealed class StubWebhookSettingsQueries(string? publicBaseUrl) : IWebhookSettingsQueries
    {
        public Task<WebhookSettingsSnapshot> GetWebhookSettingsAsync(CancellationToken cancellationToken)
            => Task.FromResult(new WebhookSettingsSnapshot(publicBaseUrl, TimeSpan.FromMinutes(15)));
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

        public Task<(string SystemPromptTemplate, string WorkerPromptTemplate, string BranchNamingInstruction)> GetPromptTemplatesAsync(
            CancellationToken cancellationToken)
            => Task.FromResult(("", "", ""));

        public Task<Foundry.Modules.Settings.Contracts.Queries.DispatchPauseState> GetDispatchPauseStateAsync(
            CancellationToken cancellationToken)
            => Task.FromResult(new Foundry.Modules.Settings.Contracts.Queries.DispatchPauseState(null, false, true));

        public Task<Foundry.Modules.Settings.Contracts.ImageBuildStatus> GetImageBuildStatusAsync(
            CancellationToken cancellationToken)
            => Task.FromResult(Foundry.Modules.Settings.Contracts.ImageBuildStatus.Idle);

        public Task<bool> GetWorkerImageInstallsDockerAsync(CancellationToken cancellationToken)
            => Task.FromResult(false);

        public Task<string> GetWorkerImageTagAsync(CancellationToken cancellationToken)
            => Task.FromResult(string.Empty);

        public Task<bool> GetWorkerImageBuildEnabledAsync(CancellationToken cancellationToken)
            => Task.FromResult(false);

        public Task<IReadOnlyDictionary<string, string>> GetWorkerImageBuildArgsAsync(
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());

        public Task<WorkerResourceLimitsSnapshot> GetWorkerResourceLimitsAsync(CancellationToken cancellationToken)
            => Task.FromResult(new WorkerResourceLimitsSnapshot(8192, 2.0, 512));

        public Task<IReadOnlyList<WorkerMountEntry>> GetWorkerMountsAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<WorkerMountEntry>>([]);


        public Task<ClaudeCodeSettingsSnapshot> GetClaudeCodeSettingsAsync(CancellationToken cancellationToken)

            => Task.FromResult(new ClaudeCodeSettingsSnapshot(string.Empty, true, []));

        public Task<HostHooksSnapshot> GetHostHooksAsync(CancellationToken cancellationToken)

            => Task.FromResult(new HostHooksSnapshot(string.Empty, []));

        public Task<IReadOnlyList<string>> GetAllowedProviderHostsAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<string>>([]);
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

        public Task<IReadOnlyList<ChecksPendingIssueInfo>> GetChecksPendingIssuesAsync(
            MonitoredRepositoryId repositoryId,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<ChecksPendingIssueInfo>>([]);

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

    private sealed class StubCredentialResolverWithFixedCredential(Credential credential) : ICredentialResolver
    {
        public Task<Credential?> ResolveAsync(string host, RepositorySlug slug, CancellationToken cancellationToken)
            => Task.FromResult<Credential?>(credential);
    }
}
