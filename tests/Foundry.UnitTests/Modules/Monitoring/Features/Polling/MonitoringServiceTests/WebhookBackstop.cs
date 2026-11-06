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
    public async Task WhenHookGoesSilent_ReRegistersItOnceWithTheServerSideToken()
    {
        // Arrange
        MonitoredRepositoryId repoId = await SeedRepoAsync(active: true, lastPolledAt: Now.AddMinutes(-16));
        CountingIssueProvider provider = new([NewIssue(5)]);
        FixedProviderFactory factory = new(provider);
        MonitoringService service = BuildService(BuildServiceProvider(
            provider,
            ingressConfigured: true,
            serverToken: "server_side_token",
            registerWebhooks: true,
            factory: factory));

        // Act
        await service.ExecuteTickAsync(Now, TestContext.Current.CancellationToken);

        // Assert — the re-registration succeeded, so the hook waits for its next delivery.
        provider.UpsertWebhookCallCount.ShouldBe(1);
        factory.Tokens.ShouldContain("server_side_token");
        (await LoadAsync(repoId)).WebhookStatus.ShouldBe(WebhookStatus.Pending);
    }

    [Fact]
    public async Task WhenHookGoesSilentWithoutServerSideToken_MakesNoRegistrationCallAndStaysSilent()
    {
        // Arrange
        MonitoredRepositoryId repoId = await SeedRepoAsync(active: true, lastPolledAt: Now.AddMinutes(-16));
        CountingIssueProvider provider = new([NewIssue(5)]);
        MonitoringService service = BuildService(BuildServiceProvider(
            provider,
            ingressConfigured: true,
            registerWebhooks: true));

        // Act
        await service.ExecuteTickAsync(Now, TestContext.Current.CancellationToken);

        // Assert
        provider.UpsertWebhookCallCount.ShouldBe(0);
        (await LoadAsync(repoId)).WebhookStatus.ShouldBe(WebhookStatus.Silent);
    }

    [Fact]
    public async Task WhenAlreadySilent_ADueScheduledPollDoesNotRetryRegistration()
    {
        // Arrange — the hook went Silent earlier and its one re-registration did not help.
        MonitoredRepositoryId repoId = await SeedRepoAsync(active: true, lastPolledAt: Now.AddMinutes(-16), silent: true);
        CountingIssueProvider provider = new([NewIssue(5)]);
        MonitoringService service = BuildService(BuildServiceProvider(
            provider,
            ingressConfigured: true,
            serverToken: "server_side_token",
            registerWebhooks: true));

        // Act
        await service.ExecuteTickAsync(Now, TestContext.Current.CancellationToken);

        // Assert
        provider.UpsertWebhookCallCount.ShouldBe(0);
        (await LoadAsync(repoId)).WebhookStatus.ShouldBe(WebhookStatus.Silent);
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

    [Fact]
    public async Task WhenScheduledPollOfActiveGitLabHookFindsMappedChange_BecomesSilent()
    {
        // Arrange — an issue Foundry does not know yet: GitLab has an Issue event for that.
        MonitoredRepositoryId repoId = await SeedRepoAsync(active: true, lastPolledAt: Now.AddMinutes(-16));
        CountingIssueProvider provider = new([NewIssue(5)]);
        MonitoringService service = BuildService(BuildServiceProvider(provider, ingressConfigured: true, gitLab: true));

        // Act
        await service.ExecuteTickAsync(Now, TestContext.Current.CancellationToken);

        // Assert
        (await LoadAsync(repoId)).WebhookStatus.ShouldBe(WebhookStatus.Silent);
    }

    [Fact]
    public async Task WhenScheduledPollOfActiveGitLabHookFindsBranchRuleChange_StaysActive()
    {
        // Arrange — the protected-branch rules changed; GitLab has no webhook event for that.
        MonitoredRepositoryId repoId = await SeedRepoAsync(
            active: true,
            lastPolledAt: Now.AddMinutes(-16),
            eligibility: new RepositoryEligibility.Eligible());
        CountingIssueProvider provider = new([]);
        MonitoringService service = BuildService(BuildServiceProvider(
            provider,
            ingressConfigured: true,
            gitLab: true,
            eligibilityAfterEvaluation: BranchRulesViolated()));

        // Act
        await service.ExecuteTickAsync(Now, TestContext.Current.CancellationToken);

        // Assert
        MonitoredRepository repo = await LoadAsync(repoId);
        repo.Eligibility.ShouldBeOfType<RepositoryEligibility.Ineligible>();
        repo.WebhookStatus.ShouldBe(WebhookStatus.Active);
    }

    [Fact]
    public async Task WhenScheduledPollOfActiveGitHubHookFindsBranchRuleChange_BecomesSilent()
    {
        // Arrange — GitHub announces branch-rule changes (branch_protection_rule, repository_ruleset).
        MonitoredRepositoryId repoId = await SeedRepoAsync(
            active: true,
            lastPolledAt: Now.AddMinutes(-16),
            eligibility: new RepositoryEligibility.Eligible());
        CountingIssueProvider provider = new([]);
        MonitoringService service = BuildService(BuildServiceProvider(
            provider,
            ingressConfigured: true,
            eligibilityAfterEvaluation: BranchRulesViolated()));

        // Act
        await service.ExecuteTickAsync(Now, TestContext.Current.CancellationToken);

        // Assert
        (await LoadAsync(repoId)).WebhookStatus.ShouldBe(WebhookStatus.Silent);
    }

    [Fact]
    public async Task WhenBranchRulesBecomeUnreadable_StaysActive()
    {
        // Arrange — a transient read failure is not a branch-rule change.
        MonitoredRepositoryId repoId = await SeedRepoAsync(
            active: true,
            lastPolledAt: Now.AddMinutes(-16),
            eligibility: new RepositoryEligibility.Eligible());
        CountingIssueProvider provider = new([]);
        MonitoringService service = BuildService(BuildServiceProvider(
            provider,
            ingressConfigured: true,
            eligibilityAfterEvaluation: new RepositoryEligibility.Unreachable(UnreachableReason.BranchRulesUnavailable)));

        // Act
        await service.ExecuteTickAsync(Now, TestContext.Current.CancellationToken);

        // Assert
        (await LoadAsync(repoId)).WebhookStatus.ShouldBe(WebhookStatus.Active);
    }

    [Fact]
    public async Task WhenScheduledPollOfActiveGitLabHookFindsDependencyLinkChange_StaysActive()
    {
        // Arrange — tracked issue 5 gained blocker 6; GitLab has no webhook event for issue links.
        MonitoredRepositoryId repoId = await SeedRepoAsync(active: true, lastPolledAt: Now.AddMinutes(-16));
        CountingIssueProvider provider = new([], dependencies: [6]);
        MonitoringService service = BuildService(BuildServiceProvider(
            provider,
            ingressConfigured: true,
            gitLab: true,
            dispatchCandidates: new HashSet<int> { 5 }));

        // Act
        await service.ExecuteTickAsync(Now, TestContext.Current.CancellationToken);

        // Assert
        provider.GetDependenciesCallCount.ShouldBe(1);
        (await LoadAsync(repoId)).WebhookStatus.ShouldBe(WebhookStatus.Active);
    }

    [Fact]
    public async Task OnFirstTickAfterStartup_PollsActiveRepositoryNotYetDue()
    {
        // Arrange — polled 5 min ago, well within the 15 min Backstop Interval.
        MonitoredRepositoryId repoId = await SeedRepoAsync(active: true, lastPolledAt: Now.AddMinutes(-5));
        CountingIssueProvider provider = new([], dependencies: [6]);
        MonitoringService service = BuildService(
            BuildServiceProvider(provider, ingressConfigured: true, dispatchCandidates: new HashSet<int> { 5 }),
            startupCatchUp: true);

        // Act
        await service.ExecuteTickAsync(Now, TestContext.Current.CancellationToken);

        // Assert — a full scheduled poll: eligibility (branch rules), listing and dependency passes.
        provider.GetIssuesCallCount.ShouldBe(1);
        provider.GetDependenciesCallCount.ShouldBe(1);
        (await LoadAsync(repoId)).LastPolledAt.ShouldBe(Now);
        service.StartupCatchUpPending.ShouldBeFalse();
    }

    [Fact]
    public async Task AfterFirstTick_LaterTicksPollOnlyDueRepositories()
    {
        // Arrange
        MonitoredRepositoryId repoId = await SeedRepoAsync(active: true, lastPolledAt: Now.AddMinutes(-5));
        CountingIssueProvider provider = new([]);
        MonitoringService service = BuildService(BuildServiceProvider(provider, ingressConfigured: true), startupCatchUp: true);
        await service.ExecuteTickAsync(Now, TestContext.Current.CancellationToken);

        // Act — the next heartbeat; the Backstop Interval has not elapsed since the catch-up.
        await service.ExecuteTickAsync(Now.AddSeconds(30), TestContext.Current.CancellationToken);

        // Assert
        provider.GetIssuesCallCount.ShouldBe(1);
        (await LoadAsync(repoId)).LastPolledAt.ShouldBe(Now);
    }

    [Fact]
    public async Task WhenFirstTickPollFails_RepositoryReturnsToNormalSchedule()
    {
        // Arrange
        MonitoredRepositoryId repoId = await SeedRepoAsync(active: true, lastPolledAt: Now.AddMinutes(-5));
        CountingIssueProvider provider = new([]) { ListingError = new Error("Test.Failure", "listing failed") };
        MonitoringService service = BuildService(BuildServiceProvider(provider, ingressConfigured: true), startupCatchUp: true);
        await service.ExecuteTickAsync(Now, TestContext.Current.CancellationToken);

        // Act — later ticks inside the Backstop Interval measured from the original poll.
        await service.ExecuteTickAsync(Now.AddSeconds(30), TestContext.Current.CancellationToken);
        await service.ExecuteTickAsync(Now.AddMinutes(5), TestContext.Current.CancellationToken);

        // Assert — the failed catch-up is not retried; the repository waits for its scheduled poll.
        provider.GetIssuesCallCount.ShouldBe(1);
        (await LoadAsync(repoId)).LastPolledAt.ShouldBe(Now.AddMinutes(-5));
    }

    [Fact]
    public async Task OnFirstTickAfterStartup_NeverPolledRepositoryIsPolledOnce()
    {
        // Arrange — a repository that has never been polled (LastPolledAt null).
        MonitoredRepositoryId repoId = await SeedNeverPolledRepoAsync();
        CountingIssueProvider provider = new([]);
        MonitoringService service = BuildService(BuildServiceProvider(provider, ingressConfigured: false), startupCatchUp: true);

        // Act
        await service.ExecuteTickAsync(Now, TestContext.Current.CancellationToken);

        // Assert
        provider.GetIssuesCallCount.ShouldBe(1);
        (await LoadAsync(repoId)).LastPolledAt.ShouldBe(Now);
    }

    [Fact]
    public async Task WhenCatchUpPollOfActiveHookFindsMappedChange_StaysActive()
    {
        // Arrange — the listing returns an issue Foundry does not know yet: lost to downtime.
        MonitoredRepositoryId repoId = await SeedRepoAsync(active: true, lastPolledAt: Now.AddMinutes(-5));
        CountingIssueProvider provider = new([NewIssue(5)]);
        MonitoringService service = BuildService(
            BuildServiceProvider(
                provider,
                ingressConfigured: true,
                serverToken: "server_side_token",
                registerWebhooks: true),
            startupCatchUp: true);

        // Act
        await service.ExecuteTickAsync(Now, TestContext.Current.CancellationToken);

        // Assert — no missed change recorded and no silent-hook re-registration.
        provider.GetIssuesCallCount.ShouldBe(1);
        provider.UpsertWebhookCallCount.ShouldBe(0);
        (await LoadAsync(repoId)).WebhookStatus.ShouldBe(WebhookStatus.Active);
    }

    private async Task<MonitoredRepositoryId> SeedNeverPolledRepoAsync()
    {
        await using FoundryDbContext db = CreateDbContext();
        MonitoredRepository repo = MonitoredRepository.Create(
            RepositorySlug.Create("owner/repo").ValueOrThrow(),
            "github.com",
            null).ValueOrThrow();
        repo.SetWriteProbeVerdict(new WriteProbeVerdict.Granted());
        db.Set<MonitoredRepository>().Add(repo);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return repo.Id;
    }

    private static RepositoryEligibility BranchRulesViolated() =>
        new RepositoryEligibility.Ineligible([EligibilityViolation.AllowForcePushes()]);

    private static ProviderIssue NewIssue(int number) =>
        new(number, $"Issue {number}", "user", $"https://github.com/owner/repo/issues/{number}", ["foundry"], "feature");

    private FoundryDbContext CreateDbContext()
    {
        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .Options;
        return new FoundryDbContext(options);
    }

    private async Task<MonitoredRepositoryId> SeedRepoAsync(
        bool active,
        DateTimeOffset lastPolledAt,
        bool isActive = true,
        RepositoryEligibility? eligibility = null,
        bool silent = false)
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

        if (silent)
        {
            repo.RecordMissedChange();
        }

        if (!isActive)
        {
            repo.Update(null, isActive: false, MonitoredRepository.DefaultMaxConcurrentWorkers);
        }

        if (eligibility is not null)
        {
            repo.SetEligibility(eligibility);
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

    /// <summary>
    /// Builds the service in its steady state (startup catch-up already consumed) unless
    /// <paramref name="startupCatchUp"/> asks for a freshly started one.
    /// </summary>
    private static MonitoringService BuildService(ServiceProvider serviceProvider, bool startupCatchUp = false) =>
        new(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            serviceProvider.GetRequiredService<WebhookNudgeQueue>(),
            TimeProvider.System,
            NullLogger<MonitoringService>.Instance)
        {
            StartupCatchUpPending = startupCatchUp,
        };

    private ServiceProvider BuildServiceProvider(
        CountingIssueProvider provider,
        bool ingressConfigured,
        bool gitLab = false,
        RepositoryEligibility? eligibilityAfterEvaluation = null,
        IReadOnlySet<int>? dispatchCandidates = null,
        string? serverToken = null,
        bool registerWebhooks = false,
        FixedProviderFactory? factory = null)
    {
        SqliteConnection connection = _connection;
        ServiceCollection services = new();

        services.AddScoped<FoundryDbContext>(_ => new FoundryDbContext(
            new DbContextOptionsBuilder<FoundryDbContext>().UseSqlite(connection).Options));
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<FoundryDbContext>());
        services.AddLogging();
        services.AddScoped<IIssueQueries>(_ => new StubIssueQueries(dispatchCandidates ?? new HashSet<int>()));
        services.AddScoped<IDomainEventDispatcher, NullDomainEventDispatcher>();
        services.AddScoped<IIntegrationEventDispatcher, NullIntegrationEventDispatcher>();
        services.AddScoped<IRepositoryEligibilityEvaluator>(_ => new FixedRepositoryEligibilityEvaluator(eligibilityAfterEvaluation));
        services.AddScoped<IIssueProviderFactory>(_ => factory ?? new FixedProviderFactory(provider));

        Credential credential = gitLab
            ? GitLabCredential.Create("my-group", "glpat_test_token", BaseUrl.Create("https://gitlab.com").ValueOrThrow())
            : GitHubCredential.Create("my-org", "ghp_test_token", BaseUrl.Create("https://github.com").ValueOrThrow(), serverToken);
        services.AddScoped<ICredentialResolver>(_ => new StubCredentialResolverWithFixedCredential(credential));
        services.AddSingleton(new WebhookNudgeQueue(NullLogger<WebhookNudgeQueue>.Instance));
        services.AddScoped<RepositoryPoller>();
        services.AddScoped<IGlobalSettingsQueries>(_ => new StubSettingsQueries(30));

        services.AddSingleton(Options.Create(new WebhookIngressOptions { Port = WebhookPort }));
        services.AddScoped<IWebhookSettingsQueries>(_ => new StubWebhookSettingsQueries(
            ingressConfigured ? "https://hooks.example.com" : null));
        services.AddScoped<WebhookIngress>();

        if (registerWebhooks)
        {
            services.AddScoped<WebhookRegistrar>();
        }

        return services.BuildServiceProvider();
    }

    private sealed class FixedProviderFactory(CountingIssueProvider provider) : IIssueProviderFactory
    {
        public List<string> Tokens { get; } = [];

        public IIssueProvider CreateProvider(Credential credential, string token)
        {
            Tokens.Add(token);
            return provider;
        }
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


        public Task<Foundry.Modules.Settings.Contracts.Queries.DispatchPauseState> GetDispatchPauseStateAsync(
            CancellationToken cancellationToken)
            => Task.FromResult(new Foundry.Modules.Settings.Contracts.Queries.DispatchPauseState(null, false, true));

        public Task<Foundry.Modules.Settings.Contracts.ImageBuildStatus> GetImageBuildStatusAsync(
            CancellationToken cancellationToken)
            => Task.FromResult(Foundry.Modules.Settings.Contracts.ImageBuildStatus.Idle);

        public Task<bool> GetWorkerImageInstallsDockerAsync(CancellationToken cancellationToken)
            => Task.FromResult(false);


        public Task<bool> GetWorkerImageInstallsDotnetAsync(CancellationToken cancellationToken)

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

        public Task<ClaudeCodeSettingsSnapshot> GetClaudeCodeSettingsAsync(CancellationToken cancellationToken)

            => Task.FromResult(new ClaudeCodeSettingsSnapshot(true, []));

        public Task<IReadOnlyList<string>> GetAllowedProviderHostsAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<string>>([]);
    }

    private sealed class StubIssueQueries(IReadOnlySet<int> dispatchCandidates) : IIssueQueries
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

        public Task<IReadOnlyList<RebasePendingIssueInfo>> GetRebasePendingIssuesAsync(
            MonitoredRepositoryId repositoryId,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<RebasePendingIssueInfo>>([]);

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
            return Task.FromResult(dispatchCandidates);
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

    /// <summary>Stores <paramref name="eligibility"/> when supplied; otherwise leaves eligibility untouched.</summary>
    private sealed class FixedRepositoryEligibilityEvaluator(RepositoryEligibility? eligibility) : IRepositoryEligibilityEvaluator
    {
        public Task EvaluateFullyAndStoreAsync(
            MonitoredRepository repo,
            DateTimeOffset now,
            CancellationToken cancellationToken)
            => EvaluateBranchRulesAndStoreAsync(repo, cancellationToken);

        public Task EvaluateBranchRulesAndStoreAsync(
            MonitoredRepository repo,
            CancellationToken cancellationToken)
        {
            if (eligibility is not null)
            {
                repo.SetEligibility(eligibility);
            }

            return Task.CompletedTask;
        }
    }

    private sealed class StubCredentialResolverWithFixedCredential(Credential credential) : ICredentialResolver
    {
        public Task<Credential?> ResolveAsync(string host, RepositorySlug slug, CancellationToken cancellationToken)
            => Task.FromResult<Credential?>(credential);
    }
}
