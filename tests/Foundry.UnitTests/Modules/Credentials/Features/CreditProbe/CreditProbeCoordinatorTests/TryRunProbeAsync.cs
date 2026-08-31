using Foundry.Modules.Credentials.Domain.Entities;
using Foundry.Modules.Credentials.Domain.ValueObjects;
using Foundry.Modules.Credentials.Features.CreditProbe;
using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;
using Foundry.Shared.Infrastructure.Outbox;
using Foundry.Testing;
using Foundry.UnitTests.Fakes.Credentials;
using Foundry.UnitTests.Fakes.Workers;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Credentials.Features.CreditProbe.CreditProbeCoordinatorTests;

public sealed class TryRunProbeAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _serviceProvider;

    public TryRunProbeAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        _serviceProvider = BuildServiceProvider(
            _connection,
            orchestrator: new FakeCredentialsOrchestrator(),
            classifier: new FakeProbeOutcomeClassifier(new ProbeOutcome.Available()),
            eventProcessor: new CapturingIntegrationEventProcessor(),
            probeIntervalMinutes: 60,
            isLoginActive: false);

        using IServiceScope setup = _serviceProvider.CreateScope();
        setup.ServiceProvider.GetRequiredService<FoundryDbContext>().Database.EnsureCreated();
    }

    public async ValueTask DisposeAsync()
    {
        await _serviceProvider.DisposeAsync();
        await _connection.DisposeAsync();
    }

    // ---------------------------------------------------------------------------
    // Helper: build a ServiceProvider with scripted dependencies
    // ---------------------------------------------------------------------------

    private static ServiceProvider BuildServiceProvider(
        SqliteConnection connection,
        FakeCredentialsOrchestrator orchestrator,
        IProbeOutcomeClassifier classifier,
        CapturingIntegrationEventProcessor eventProcessor,
        int probeIntervalMinutes,
        bool isLoginActive,
        CapturingSystemNotificationBroadcaster? broadcaster = null)
    {
        ServiceCollection services = new();

        // EF / outbox
        services.AddScoped<IntegrationEventCollector>();
        services.AddScoped<OutboxSaveChangesInterceptor>();
        services.AddScoped<IIntegrationEventDispatcher, OutboxIntegrationEventDispatcher>();
        services.AddDbContext<FoundryDbContext>((sp, options) =>
        {
            options.UseSqlite(connection);
            options.AddInterceptors(sp.GetRequiredService<OutboxSaveChangesInterceptor>());
        });
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<FoundryDbContext>());

        // Probe dependencies
        services.AddSingleton<Foundry.Modules.Credentials.Infrastructure.Orchestration.ICredentialsOrchestrator>(orchestrator);
        services.AddSingleton(classifier);

        // IIntegrationEventProcessor is scoped in production; the coordinator resolves it from
        // its per-invocation scope. Register as scoped here so the scope factory returns the
        // capturing instance — same instance is returned for the lifetime of each scope.
        services.AddScoped<IIntegrationEventProcessor>(_ => eventProcessor);
        services.AddSingleton<StubLoginSessionState>(new StubLoginSessionState(isLoginActive));
        services.AddSingleton<Foundry.Modules.Credentials.Features.Login.ILoginSessionState>(
            sp => sp.GetRequiredService<StubLoginSessionState>());

        // IGlobalSettingsQueries is scoped in production; use scoped here too so the
        // coordinator resolves it from its per-invocation scope (not at construction time).
        services.AddScoped<Foundry.Modules.Settings.Contracts.Queries.IGlobalSettingsQueries>(
            _ => new StubGlobalSettingsQueries(probeIntervalMinutes));

        // ISystemNotificationBroadcaster is scoped — resolved from the per-invocation scope.
        CapturingSystemNotificationBroadcaster resolvedBroadcaster = broadcaster ?? new CapturingSystemNotificationBroadcaster();
        services.AddScoped<ISystemNotificationBroadcaster>(_ => resolvedBroadcaster);

        return services.BuildServiceProvider();
    }

    private static CreditProbeCoordinator BuildCoordinator(ServiceProvider sp)
        => new(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<Foundry.Modules.Credentials.Infrastructure.Orchestration.ICredentialsOrchestrator>(),
            sp.GetRequiredService<IProbeOutcomeClassifier>(),
            sp.GetRequiredService<Foundry.Modules.Credentials.Features.Login.ILoginSessionState>(),
            NullLogger<CreditProbeCoordinator>.Instance);

    private async Task SeedBlockedAccountAsync(DateTimeOffset nextProbeAt)
    {
        await using AsyncServiceScope scope = _serviceProvider.CreateAsyncScope();
        FoundryDbContext db = scope.ServiceProvider.GetRequiredService<FoundryDbContext>();
        ClaudeAccount account = ClaudeAccount.Create();
        account.BlockSpend(nextProbeAt);
        db.Set<ClaudeAccount>().Add(account);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task SeedAvailableAccountAsync()
    {
        await using AsyncServiceScope scope = _serviceProvider.CreateAsyncScope();
        FoundryDbContext db = scope.ServiceProvider.GetRequiredService<FoundryDbContext>();
        ClaudeAccount account = ClaudeAccount.Create();
        db.Set<ClaudeAccount>().Add(account);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task SeedGlobalSettingsAsync()
    {
        await using AsyncServiceScope scope = _serviceProvider.CreateAsyncScope();
        FoundryDbContext db = scope.ServiceProvider.GetRequiredService<FoundryDbContext>();
        GlobalSettings settings = GlobalSettings.Create();
        db.Set<GlobalSettings>().Add(settings);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task<ClaudeAccount?> ReadAccountAsync()
    {
        await using AsyncServiceScope scope = _serviceProvider.CreateAsyncScope();
        FoundryDbContext db = scope.ServiceProvider.GetRequiredService<FoundryDbContext>();
        return await db.Set<ClaudeAccount>().FirstOrDefaultAsync(TestContext.Current.CancellationToken);
    }

    private async Task<List<OutboxMessage>> ReadOutboxAsync()
    {
        await using AsyncServiceScope scope = _serviceProvider.CreateAsyncScope();
        FoundryDbContext db = scope.ServiceProvider.GetRequiredService<FoundryDbContext>();
        return await db.Set<OutboxMessage>().ToListAsync(TestContext.Current.CancellationToken);
    }

    // ---------------------------------------------------------------------------
    // Inner helpers
    // ---------------------------------------------------------------------------

    private sealed class StubLoginSessionState(bool isLoginActive)
        : Foundry.Modules.Credentials.Features.Login.ILoginSessionState
    {
        public bool IsLoginActive => isLoginActive;
    }

    private sealed class StubGlobalSettingsQueries(int probeIntervalMinutes)
        : Foundry.Modules.Settings.Contracts.Queries.IGlobalSettingsQueries
    {
        public Task<int> GetProbeIntervalMinutesAsync(CancellationToken cancellationToken)
            => Task.FromResult(probeIntervalMinutes);

        public Task<int> GetPollIntervalSecondsAsync(CancellationToken cancellationToken)
            => Task.FromResult(30);

        // Remaining members not exercised by these tests
        public Task<Foundry.Modules.Settings.Contracts.GlobalSettingsSummary?> GetSettingsAsync(
            CancellationToken cancellationToken)
            => Task.FromResult<Foundry.Modules.Settings.Contracts.GlobalSettingsSummary?>(null);

        public Task<int> GetMaxConcurrentAsync(CancellationToken cancellationToken)
            => Task.FromResult(1);

        public Task<int> GetTimeoutMinutesAsync(CancellationToken cancellationToken)
            => Task.FromResult(120);

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
            => Task.FromResult<IReadOnlyDictionary<string, string>>(
                new Dictionary<string, string>());

        public Task<IReadOnlyList<string>> GetAllowedProviderHostsAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<string>>([]);
    }

    internal sealed class CapturingIntegrationEventProcessor : IIntegrationEventProcessor
    {
        private readonly List<IIntegrationEvent> _captured = [];

        public IReadOnlyList<IIntegrationEvent> Captured => _captured;

        public Task ProcessAsync(Guid eventId, IIntegrationEvent @event, CancellationToken cancellationToken)
        {
            _captured.Add(@event);
            return Task.CompletedTask;
        }
    }

    internal sealed class CapturingSystemNotificationBroadcaster : ISystemNotificationBroadcaster
    {
        private readonly List<SystemNotification> _notifications = [];

        public IReadOnlyList<SystemNotification> SentNotifications => _notifications;

        public Task SendAsync(SystemNotification notification, CancellationToken cancellationToken)
        {
            _notifications.Add(notification);
            return Task.CompletedTask;
        }
    }

    private sealed class CapturingLoggerAdapter<T>(CapturingLogger inner) : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            inner.Log(logLevel, eventId, state, exception, formatter);
        }
    }

    // ---------------------------------------------------------------------------
    // Tests
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task WhenAvailable_RestoresSpendAndPublishesCreditsRestored()
    {
        // Arrange
        await SeedBlockedAccountAsync(DateTimeOffset.UtcNow.AddHours(1));

        ServiceProvider sp = BuildServiceProvider(
            _connection,
            orchestrator: new FakeCredentialsOrchestrator().WithCreditProbeLogs("ok"),
            classifier: new FakeProbeOutcomeClassifier(new ProbeOutcome.Available()),
            eventProcessor: new CapturingIntegrationEventProcessor(),
            probeIntervalMinutes: 60,
            isLoginActive: false);
        await using (sp)
        {
            CreditProbeCoordinator sut = BuildCoordinator(sp);

            // Act
            CreditProbeResult result = await sut.TryRunProbeAsync(TestContext.Current.CancellationToken);

            // Assert — result
            result.ShouldBeOfType<CreditProbeResult.Restored>();
        }

        // Assert — account restored
        ClaudeAccount? account = await ReadAccountAsync();
        account.ShouldNotBeNull();
        account.SpendState.ShouldBeOfType<SpendState.Available>();

        // Assert — CreditsRestored outbox row
        List<OutboxMessage> outbox = await ReadOutboxAsync();
        outbox.ShouldContain(m => m.Type.Contains("CreditsRestored", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WhenAvailable_PublishesCreditsRestoredOnce()
    {
        // Arrange
        await SeedBlockedAccountAsync(DateTimeOffset.UtcNow.AddHours(1));

        CapturingIntegrationEventProcessor processor = new();
        ServiceProvider sp = BuildServiceProvider(
            _connection,
            orchestrator: new FakeCredentialsOrchestrator().WithCreditProbeLogs("ok"),
            classifier: new FakeProbeOutcomeClassifier(new ProbeOutcome.Available()),
            eventProcessor: processor,
            probeIntervalMinutes: 60,
            isLoginActive: false);
        await using (sp)
        {
            CreditProbeCoordinator sut = BuildCoordinator(sp);

            // Act
            await sut.TryRunProbeAsync(TestContext.Current.CancellationToken);
        }

        // Assert — exactly one CreditsRestored outbox row (not two)
        List<OutboxMessage> outbox = await ReadOutboxAsync();
        outbox.Count(m => m.Type.Contains("CreditsRestored", StringComparison.Ordinal)).ShouldBe(1);
    }

    [Fact]
    public async Task WhenStillBlocked_RearmsProbeAndDoesNotPublish()
    {
        // Arrange
        DateTimeOffset originalNextProbeAt = DateTimeOffset.UtcNow.AddMinutes(5);
        await SeedBlockedAccountAsync(originalNextProbeAt);

        ServiceProvider sp = BuildServiceProvider(
            _connection,
            orchestrator: new FakeCredentialsOrchestrator().WithCreditProbeLogs("credits exhausted"),
            classifier: new FakeProbeOutcomeClassifier(new ProbeOutcome.CreditsStillBlocked()),
            eventProcessor: new CapturingIntegrationEventProcessor(),
            probeIntervalMinutes: 60,
            isLoginActive: false);
        await using (sp)
        {
            CreditProbeCoordinator sut = BuildCoordinator(sp);

            // Act
            CreditProbeResult result = await sut.TryRunProbeAsync(TestContext.Current.CancellationToken);

            // Assert — result
            result.ShouldBeOfType<CreditProbeResult.StillBlocked>();
        }

        // Assert — account is still Blocked (probe re-armed)
        ClaudeAccount? account = await ReadAccountAsync();
        account.ShouldNotBeNull();
        account.SpendState.ShouldBeOfType<SpendState.Blocked>();

        // Assert — no outbox row
        List<OutboxMessage> outbox = await ReadOutboxAsync();
        outbox.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenUsageLimited_SetsResetsAtAndDeliversDispatchPausedAndClearsCreditBlock()
    {
        // Arrange
        await SeedBlockedAccountAsync(DateTimeOffset.UtcNow.AddHours(1));
        await SeedGlobalSettingsAsync();

        DateTimeOffset resetsAt = DateTimeOffset.UtcNow.AddHours(2);
        CapturingIntegrationEventProcessor processor = new();
        ServiceProvider sp = BuildServiceProvider(
            _connection,
            orchestrator: new FakeCredentialsOrchestrator().WithCreditProbeLogs("rate limited"),
            classifier: new FakeProbeOutcomeClassifier(new ProbeOutcome.UsageLimited(resetsAt)),
            eventProcessor: processor,
            probeIntervalMinutes: 60,
            isLoginActive: false);
        await using (sp)
        {
            CreditProbeCoordinator sut = BuildCoordinator(sp);

            // Act
            CreditProbeResult result = await sut.TryRunProbeAsync(TestContext.Current.CancellationToken);

            // Assert — result
            CreditProbeResult.UsageLimited typedResult = result.ShouldBeOfType<CreditProbeResult.UsageLimited>();
            typedResult.ResetsAt.ShouldBe(resetsAt, tolerance: TimeSpan.FromSeconds(1));

            // Assert — DispatchPaused delivered directly (ephemeral broadcast)
            processor.Captured.ShouldContain(e => e is Foundry.Modules.Workers.Contracts.DispatchPaused);
        }

        // Assert — credit block is cleared (account is Available)
        ClaudeAccount? account = await ReadAccountAsync();
        account.ShouldNotBeNull();
        account.SpendState.ShouldBeOfType<SpendState.Available>();

        // Assert — CreditsRestored outbox row written (credit block cleared)
        List<OutboxMessage> outbox = await ReadOutboxAsync();
        outbox.ShouldContain(m => m.Type.Contains("CreditsRestored", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WhenUsageLimited_PublishesCreditsRestoredOnce()
    {
        // Arrange
        await SeedBlockedAccountAsync(DateTimeOffset.UtcNow.AddHours(1));
        await SeedGlobalSettingsAsync();

        DateTimeOffset resetsAt = DateTimeOffset.UtcNow.AddHours(2);
        CapturingIntegrationEventProcessor processor = new();
        ServiceProvider sp = BuildServiceProvider(
            _connection,
            orchestrator: new FakeCredentialsOrchestrator().WithCreditProbeLogs("rate limited"),
            classifier: new FakeProbeOutcomeClassifier(new ProbeOutcome.UsageLimited(resetsAt)),
            eventProcessor: processor,
            probeIntervalMinutes: 60,
            isLoginActive: false);
        await using (sp)
        {
            CreditProbeCoordinator sut = BuildCoordinator(sp);

            // Act
            await sut.TryRunProbeAsync(TestContext.Current.CancellationToken);
        }

        // Assert — exactly one CreditsRestored outbox row
        List<OutboxMessage> outbox = await ReadOutboxAsync();
        outbox.Count(m => m.Type.Contains("CreditsRestored", StringComparison.Ordinal)).ShouldBe(1);
    }

    [Fact]
    public async Task WhenInfrastructureFailureFromClassifier_RearmsProbeAndNoStateChange()
    {
        // Arrange
        await SeedBlockedAccountAsync(DateTimeOffset.UtcNow.AddHours(1));

        CapturingIntegrationEventProcessor processor = new();
        ServiceProvider sp = BuildServiceProvider(
            _connection,
            orchestrator: new FakeCredentialsOrchestrator().WithCreditProbeLogs("docker error"),
            classifier: new FakeProbeOutcomeClassifier(new ProbeOutcome.InfrastructureFailure()),
            eventProcessor: processor,
            probeIntervalMinutes: 60,
            isLoginActive: false);
        await using (sp)
        {
            CreditProbeCoordinator sut = BuildCoordinator(sp);

            // Act
            CreditProbeResult result = await sut.TryRunProbeAsync(TestContext.Current.CancellationToken);

            // Assert — result
            result.ShouldBeOfType<CreditProbeResult.InfrastructureFailure>();

            // Assert — no DispatchPaused or CreditsRestored delivered directly
            processor.Captured.ShouldBeEmpty();
        }

        // Assert — account is still Blocked (no credit state change)
        ClaudeAccount? account = await ReadAccountAsync();
        account.ShouldNotBeNull();
        account.SpendState.ShouldBeOfType<SpendState.Blocked>();

        // Assert — no CreditsRestored outbox row
        List<OutboxMessage> outbox = await ReadOutboxAsync();
        outbox.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenOrchestratorFails_TreatsAsInfrastructureFailureAndRearmsProbe()
    {
        // Arrange
        await SeedBlockedAccountAsync(DateTimeOffset.UtcNow.AddHours(1));

        CapturingIntegrationEventProcessor processor = new();
        ServiceProvider sp = BuildServiceProvider(
            _connection,
            orchestrator: new FakeCredentialsOrchestrator()
                .WithCreditProbeFailure(new Error("Docker.Error", "container failed to start")),
            classifier: new FakeProbeOutcomeClassifier(new ProbeOutcome.Available()),
            eventProcessor: processor,
            probeIntervalMinutes: 60,
            isLoginActive: false);
        await using (sp)
        {
            CreditProbeCoordinator sut = BuildCoordinator(sp);

            // Act
            CreditProbeResult result = await sut.TryRunProbeAsync(TestContext.Current.CancellationToken);

            // Assert — result
            result.ShouldBeOfType<CreditProbeResult.InfrastructureFailure>();

            // Assert — no events delivered
            processor.Captured.ShouldBeEmpty();
        }

        // Assert — account still Blocked
        ClaudeAccount? account = await ReadAccountAsync();
        account.ShouldNotBeNull();
        account.SpendState.ShouldBeOfType<SpendState.Blocked>();

        // Assert — no outbox rows
        List<OutboxMessage> outbox = await ReadOutboxAsync();
        outbox.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenLoginActive_DefersProbAndRearmsProbe()
    {
        // Arrange
        await SeedBlockedAccountAsync(DateTimeOffset.UtcNow.AddHours(1));

        ServiceProvider sp = BuildServiceProvider(
            _connection,
            orchestrator: new FakeCredentialsOrchestrator(),
            classifier: new FakeProbeOutcomeClassifier(new ProbeOutcome.Available()),
            eventProcessor: new CapturingIntegrationEventProcessor(),
            probeIntervalMinutes: 60,
            isLoginActive: true);
        await using (sp)
        {
            CreditProbeCoordinator sut = BuildCoordinator(sp);

            // Act
            CreditProbeResult result = await sut.TryRunProbeAsync(TestContext.Current.CancellationToken);

            // Assert — result
            result.ShouldBeOfType<CreditProbeResult.Deferred>();
        }

        // Assert — account still Blocked (probe arm was refreshed, not restored)
        ClaudeAccount? account = await ReadAccountAsync();
        account.ShouldNotBeNull();
        account.SpendState.ShouldBeOfType<SpendState.Blocked>();

        // Assert — no outbox rows
        List<OutboxMessage> outbox = await ReadOutboxAsync();
        outbox.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenNoAccount_LogsWarningAndReturnsNoAccount()
    {
        // Arrange
        CapturingLogger logger = new();
        ServiceProvider sp = BuildServiceProvider(
            _connection,
            orchestrator: new FakeCredentialsOrchestrator(),
            classifier: new FakeProbeOutcomeClassifier(new ProbeOutcome.Available()),
            eventProcessor: new CapturingIntegrationEventProcessor(),
            probeIntervalMinutes: 60,
            isLoginActive: false);
        await using (sp)
        {
            CreditProbeCoordinator sut = new(
                sp.GetRequiredService<IServiceScopeFactory>(),
                sp.GetRequiredService<Foundry.Modules.Credentials.Infrastructure.Orchestration.ICredentialsOrchestrator>(),
                sp.GetRequiredService<IProbeOutcomeClassifier>(),
                sp.GetRequiredService<Foundry.Modules.Credentials.Features.Login.ILoginSessionState>(),
                new CapturingLoggerAdapter<CreditProbeCoordinator>(logger));

            // Act
            CreditProbeResult result = await sut.TryRunProbeAsync(TestContext.Current.CancellationToken);

            // Assert — result
            result.ShouldBeOfType<CreditProbeResult.NoAccount>();
        }

        // Assert — warning logged
        logger.Entries.ShouldContain(e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task WhenAccountNotBlocked_ReturnsNotBlocked()
    {
        // Arrange
        await SeedAvailableAccountAsync();

        ServiceProvider sp = BuildServiceProvider(
            _connection,
            orchestrator: new FakeCredentialsOrchestrator(),
            classifier: new FakeProbeOutcomeClassifier(new ProbeOutcome.Available()),
            eventProcessor: new CapturingIntegrationEventProcessor(),
            probeIntervalMinutes: 60,
            isLoginActive: false);
        await using (sp)
        {
            CreditProbeCoordinator sut = BuildCoordinator(sp);

            // Act
            CreditProbeResult result = await sut.TryRunProbeAsync(TestContext.Current.CancellationToken);

            // Assert — result
            result.ShouldBeOfType<CreditProbeResult.NotBlocked>();
        }

        // Assert — no outbox rows
        List<OutboxMessage> outbox = await ReadOutboxAsync();
        outbox.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenSecondConcurrentCaller_ReturnsAlreadyRunning()
    {
        // Arrange
        await SeedBlockedAccountAsync(DateTimeOffset.UtcNow.AddHours(1));

        // Use a blocking orchestrator to keep the semaphore held
        TaskCompletionSource probeTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        BlockingCredentialsOrchestrator blockingOrchestrator = new(probeTcs.Task);

        ServiceProvider sp = BuildServiceProvider(
            _connection,
            orchestrator: new FakeCredentialsOrchestrator(),
            classifier: new FakeProbeOutcomeClassifier(new ProbeOutcome.Available()),
            eventProcessor: new CapturingIntegrationEventProcessor(),
            probeIntervalMinutes: 60,
            isLoginActive: false);
        await using (sp)
        {
            // Replace the orchestrator with the blocking one for the coordinator
            CreditProbeCoordinator sut = new(
                sp.GetRequiredService<IServiceScopeFactory>(),
                blockingOrchestrator,
                sp.GetRequiredService<IProbeOutcomeClassifier>(),
                sp.GetRequiredService<Foundry.Modules.Credentials.Features.Login.ILoginSessionState>(),
                NullLogger<CreditProbeCoordinator>.Instance);

            // Start the first probe (will block inside RunCreditProbeAsync)
            Task<CreditProbeResult> firstProbe = sut.TryRunProbeAsync(TestContext.Current.CancellationToken);

            // Wait until the first probe is inside the orchestrator
            await blockingOrchestrator.ProbeStarted.Task;

            // Act — second caller while first is running
            CreditProbeResult secondResult = await sut.TryRunProbeAsync(TestContext.Current.CancellationToken);

            // Release the blocking probe
            probeTcs.SetResult();
            await firstProbe;

            // Assert — second caller got AlreadyRunning immediately
            secondResult.ShouldBeOfType<CreditProbeResult.AlreadyRunning>();
        }
    }

    [Fact]
    public async Task WhenStillBlocked_SendsCreditsBroadcastWithIsActiveTrue()
    {
        // Arrange
        DateTimeOffset originalNextProbeAt = DateTimeOffset.UtcNow.AddMinutes(5);
        await SeedBlockedAccountAsync(originalNextProbeAt);

        CapturingSystemNotificationBroadcaster broadcaster = new();
        ServiceProvider sp = BuildServiceProvider(
            _connection,
            orchestrator: new FakeCredentialsOrchestrator().WithCreditProbeLogs("credits exhausted"),
            classifier: new FakeProbeOutcomeClassifier(new ProbeOutcome.CreditsStillBlocked()),
            eventProcessor: new CapturingIntegrationEventProcessor(),
            probeIntervalMinutes: 60,
            isLoginActive: false,
            broadcaster: broadcaster);
        await using (sp)
        {
            CreditProbeCoordinator sut = BuildCoordinator(sp);

            // Act
            await sut.TryRunProbeAsync(TestContext.Current.CancellationToken);
        }

        // Assert — credits broadcast sent with IsActive:true (re-arm signals fresh countdown)
        broadcaster.SentNotifications.ShouldContain(n =>
            n.Category == "credits" && n.IsActive);
    }

    [Fact]
    public async Task WhenInfrastructureFailureFromClassifier_SendsCreditsBroadcastWithIsActiveTrue()
    {
        // Arrange
        await SeedBlockedAccountAsync(DateTimeOffset.UtcNow.AddHours(1));

        CapturingSystemNotificationBroadcaster broadcaster = new();
        ServiceProvider sp = BuildServiceProvider(
            _connection,
            orchestrator: new FakeCredentialsOrchestrator().WithCreditProbeLogs("docker error"),
            classifier: new FakeProbeOutcomeClassifier(new ProbeOutcome.InfrastructureFailure()),
            eventProcessor: new CapturingIntegrationEventProcessor(),
            probeIntervalMinutes: 60,
            isLoginActive: false,
            broadcaster: broadcaster);
        await using (sp)
        {
            CreditProbeCoordinator sut = BuildCoordinator(sp);

            // Act
            await sut.TryRunProbeAsync(TestContext.Current.CancellationToken);
        }

        // Assert — credits broadcast sent with IsActive:true (probe re-armed)
        broadcaster.SentNotifications.ShouldContain(n =>
            n.Category == "credits" && n.IsActive);
    }

    [Fact]
    public async Task WhenOrchestratorFails_SendsCreditsBroadcastWithIsActiveTrue()
    {
        // Arrange
        await SeedBlockedAccountAsync(DateTimeOffset.UtcNow.AddHours(1));

        CapturingSystemNotificationBroadcaster broadcaster = new();
        ServiceProvider sp = BuildServiceProvider(
            _connection,
            orchestrator: new FakeCredentialsOrchestrator()
                .WithCreditProbeFailure(new Error("Docker.Error", "container failed to start")),
            classifier: new FakeProbeOutcomeClassifier(new ProbeOutcome.Available()),
            eventProcessor: new CapturingIntegrationEventProcessor(),
            probeIntervalMinutes: 60,
            isLoginActive: false,
            broadcaster: broadcaster);
        await using (sp)
        {
            CreditProbeCoordinator sut = BuildCoordinator(sp);

            // Act
            await sut.TryRunProbeAsync(TestContext.Current.CancellationToken);
        }

        // Assert — credits broadcast sent with IsActive:true (probe re-armed)
        broadcaster.SentNotifications.ShouldContain(n =>
            n.Category == "credits" && n.IsActive);
    }

    [Fact]
    public async Task WhenLoginActive_SendsCreditsBroadcastWithIsActiveTrue()
    {
        // Arrange
        await SeedBlockedAccountAsync(DateTimeOffset.UtcNow.AddHours(1));

        CapturingSystemNotificationBroadcaster broadcaster = new();
        ServiceProvider sp = BuildServiceProvider(
            _connection,
            orchestrator: new FakeCredentialsOrchestrator(),
            classifier: new FakeProbeOutcomeClassifier(new ProbeOutcome.Available()),
            eventProcessor: new CapturingIntegrationEventProcessor(),
            probeIntervalMinutes: 60,
            isLoginActive: true,
            broadcaster: broadcaster);
        await using (sp)
        {
            CreditProbeCoordinator sut = BuildCoordinator(sp);

            // Act
            await sut.TryRunProbeAsync(TestContext.Current.CancellationToken);
        }

        // Assert — credits broadcast sent with IsActive:true (login-deferred re-arm)
        broadcaster.SentNotifications.ShouldContain(n =>
            n.Category == "credits" && n.IsActive);
    }

    [Fact]
    public async Task WhenAvailable_DoesNotSendCreditsBroadcastFromCoordinator()
    {
        // Arrange
        await SeedBlockedAccountAsync(DateTimeOffset.UtcNow.AddHours(1));

        CapturingSystemNotificationBroadcaster broadcaster = new();
        ServiceProvider sp = BuildServiceProvider(
            _connection,
            orchestrator: new FakeCredentialsOrchestrator().WithCreditProbeLogs("ok"),
            classifier: new FakeProbeOutcomeClassifier(new ProbeOutcome.Available()),
            eventProcessor: new CapturingIntegrationEventProcessor(),
            probeIntervalMinutes: 60,
            isLoginActive: false,
            broadcaster: broadcaster);
        await using (sp)
        {
            CreditProbeCoordinator sut = BuildCoordinator(sp);

            // Act
            await sut.TryRunProbeAsync(TestContext.Current.CancellationToken);
        }

        // Assert — coordinator does NOT broadcast directly on restore paths
        // (CreditsRestored → CreditsRestoredBroadcastHandler handles that separately)
        broadcaster.SentNotifications.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenUsageLimited_DoesNotSendCreditsBroadcastFromCoordinator()
    {
        // Arrange
        await SeedBlockedAccountAsync(DateTimeOffset.UtcNow.AddHours(1));
        await SeedGlobalSettingsAsync();

        CapturingSystemNotificationBroadcaster broadcaster = new();
        DateTimeOffset resetsAt = DateTimeOffset.UtcNow.AddHours(2);
        ServiceProvider sp = BuildServiceProvider(
            _connection,
            orchestrator: new FakeCredentialsOrchestrator().WithCreditProbeLogs("rate limited"),
            classifier: new FakeProbeOutcomeClassifier(new ProbeOutcome.UsageLimited(resetsAt)),
            eventProcessor: new CapturingIntegrationEventProcessor(),
            probeIntervalMinutes: 60,
            isLoginActive: false,
            broadcaster: broadcaster);
        await using (sp)
        {
            CreditProbeCoordinator sut = BuildCoordinator(sp);

            // Act
            await sut.TryRunProbeAsync(TestContext.Current.CancellationToken);
        }

        // Assert — coordinator does NOT broadcast a credits notification on usage-limited restore
        // (CreditsRestored → CreditsRestoredBroadcastHandler handles the banner clear separately)
        broadcaster.SentNotifications.ShouldBeEmpty();
    }

    /// <summary>
    /// Blocks inside <see cref="RunCreditProbeAsync"/> until the test releases the gate,
    /// allowing the single-flight test to reproduce a concurrent second caller.
    /// </summary>
    private sealed class BlockingCredentialsOrchestrator(Task gate)
        : Foundry.Modules.Credentials.Infrastructure.Orchestration.ICredentialsOrchestrator
    {
        public TaskCompletionSource ProbeStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<Result<string>> RunCreditProbeAsync(
            Foundry.Modules.Credentials.Features.CreditProbe.CreditProbeSpec spec,
            CancellationToken cancellationToken)
        {
            ProbeStarted.TrySetResult();
            await gate;
            return Result<string>.Ok("ok");
        }

        // Unused
        public Task<Result<string>> StartLoginContainerAsync(
            Foundry.Modules.Credentials.Features.Login.LoginContainerSpec spec,
            CancellationToken cancellationToken)
            => Task.FromResult(Result<string>.Ok(""));

        public Task DeliverLoginCodeAsync(string containerId, string code, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<Result<Foundry.Modules.Credentials.Infrastructure.Orchestration.AccountIdentity>> GetCredentialVolumeAuthStatusAsync(
            CancellationToken cancellationToken)
            => Task.FromResult(
                Result<Foundry.Modules.Credentials.Infrastructure.Orchestration.AccountIdentity>.Fail(
                    new Error("test", "not used")));

        public IAsyncEnumerable<string> StreamLogsAsync(string containerId, CancellationToken cancellationToken)
            => AsyncEnumerable.Empty<string>();

        public Task StopContainerAsync(string containerId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task RemoveContainerAsync(string containerId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<IReadOnlyList<string>> ListTransientContainersAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<string>>([]);

        public Task<IReadOnlyList<string>> ListExitedTransientContainersAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<string>>([]);

        public Task SeedOnboardingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task EnsureCredentialVolumeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
