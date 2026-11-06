using Foundry.Modules.Credentials.Domain.Entities;
using Foundry.Modules.Credentials.Features.CreditProbe;
using Foundry.Modules.Credentials.Features.Login;
using Foundry.Modules.Credentials.Features.SessionCheck;
using Foundry.Modules.Credentials.Features.Usage;
using Foundry.Modules.Credentials.Infrastructure.Orchestration;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.UnitTests.Fakes.Credentials;
using Foundry.WebApi.Persistence;

using Microsoft.Extensions.Logging;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Credentials.Features.Usage.UsagePollCoordinatorTests;

public sealed class PollAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _serviceProvider;

    /// <summary>
    /// Minimal stream-json from <c>claude -p "/usage"</c>: system init + the synthetic assistant
    /// message carrying a <c>usage_report</c> with populated <c>rate_limits</c> — a successful parse.
    /// </summary>
    private const string ValidUsageLog = """
        {"type":"system","subtype":"init","cwd":"/workspace","session_id":"abc123","tools":[],"mcp_servers":[],"model":"claude-opus-4-8","permissionMode":"default","apiKeySource":"none","claude_code_version":"2.1.316"}
        {"type":"assistant","message":{"id":"msg_01","type":"message","role":"assistant","model":"<synthetic>","content":[{"type":"text","text":"Current session"}]},"parent_tool_use_id":null,"session_id":"abc123","usage_report":{"session":{},"rate_limits":{"limits":[{"kind":"session","group":"session","percent":14,"resets_at":"2026-11-06T13:00:00Z","severity":"normal","is_active":false},{"kind":"weekly_all","group":"weekly","percent":65,"resets_at":"2026-11-10T09:00:00Z","severity":"normal","is_active":true}],"extra_usage":{"is_enabled":true,"monthly_limit":50000,"used_credits":16568,"utilization":null,"currency":"USD"}}}}
        {"type":"result","subtype":"success","is_error":false,"duration_ms":100,"duration_api_ms":0,"num_turns":0,"result":"","total_cost_usd":0,"session_id":"abc123"}
        """;

    /// <summary>
    /// <c>/usage</c> output whose <c>usage_report.rate_limits.limits</c> is null but which still
    /// reports extra-usage credits → RateLimitsMissing, triggering the probe-turn fallback.
    /// </summary>
    private const string RateLimitsMissingLog = """
        {"type":"system","subtype":"init","cwd":"/workspace","session_id":"abc123","tools":[],"mcp_servers":[],"model":"claude-opus-4-8","permissionMode":"default","apiKeySource":"none","claude_code_version":"2.1.399"}
        {"type":"assistant","message":{"id":"msg_01","type":"message","role":"assistant","model":"<synthetic>","content":[{"type":"text","text":"Current session"}]},"parent_tool_use_id":null,"session_id":"abc123","usage_report":{"session":{},"rate_limits":{"limits":null,"extra_usage":{"is_enabled":true,"monthly_limit":50000,"used_credits":16568,"utilization":null,"currency":"USD"}}}}
        {"type":"result","subtype":"success","is_error":false,"duration_ms":100,"duration_api_ms":0,"num_turns":0,"result":"","total_cost_usd":0,"session_id":"abc123"}
        """;

    /// <summary>
    /// Minimal stream-json from the fallback probe turn: system init + assistant message +
    /// rate_limit_event with session and weekly_all windows.
    /// </summary>
    private const string ProbeTurnLog = """
        {"type":"system","subtype":"init","cwd":"/workspace","session_id":"abc123","tools":[],"mcp_servers":[],"model":"claude-opus-4-8","permissionMode":"default","apiKeySource":"none"}
        {"type":"assistant","message":{"id":"msg_01","type":"message","role":"assistant","content":[{"type":"text","text":"ok"}],"stop_reason":"end_turn","usage":{"input_tokens":10,"output_tokens":3}},"session_id":"abc123"}
        {"type":"rate_limit_event","rate_limit_info":{"status":"active","resetsAt":1793527200,"rateLimitType":"seven_day","unifiedWindows":{"five_hour":{"utilization":0.14,"resetsAt":1793399400},"seven_day":{"utilization":0.65,"resetsAt":1793527200}}},"uuid":"abc","session_id":"abc123"}
        {"type":"result","subtype":"success","is_error":false,"duration_ms":100,"duration_api_ms":80,"num_turns":1,"result":"ok","total_cost_usd":0.001,"session_id":"abc123"}
        """;

    /// <summary>A log where no line carries a usage_report or rate_limit_event → NoUsageReport (API-key mode).</summary>
    private const string NoUsageReportLog = """
        {"type":"system","subtype":"init","cwd":"/workspace","session_id":"abc123","tools":[],"mcp_servers":[],"model":"claude-opus-4-8","permissionMode":"default","apiKeySource":"none"}
        {"type":"assistant","message":{"id":"msg_01","type":"message","role":"assistant","content":[{"type":"text","text":"ok"}],"stop_reason":"end_turn","usage":{"input_tokens":10,"output_tokens":3}},"session_id":"abc123"}
        {"type":"result","subtype":"success","is_error":false,"duration_ms":500,"duration_api_ms":400,"num_turns":1,"result":"ok","total_cost_usd":0.001,"session_id":"abc123"}
        """;

    /// <summary>Output with no stream-json line at all (CLI auth error) → Unparseable.</summary>
    private const string MalformedJsonLog = """
        Invalid API key · Please run /login
        {INVALID JSON CANNOT PARSE THIS
        """;

    public PollAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        _serviceProvider = BuildServiceProvider(
            _connection,
            new FakeCredentialsOrchestrator(),
            new FakeLoginSessionState());

        using IServiceScope setup = _serviceProvider.CreateScope();
        setup.ServiceProvider.GetRequiredService<FoundryDbContext>().Database.EnsureCreated();
    }

    public async ValueTask DisposeAsync()
    {
        await _serviceProvider.DisposeAsync();
        await _connection.DisposeAsync();
    }

    // ---------------------------------------------------------------------------
    // Setup helpers
    // ---------------------------------------------------------------------------

    private static ServiceProvider BuildServiceProvider(
        SqliteConnection connection,
        ICredentialsOrchestrator orchestrator,
        FakeLoginSessionState loginState,
        CapturingUsageSnapshotBroadcaster? broadcaster = null,
        UsageSnapshotState? state = null)
    {
        ServiceCollection services = new();

        services.AddDbContext<FoundryDbContext>((_, options) => options.UseSqlite(connection));
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<FoundryDbContext>());

        services.AddSingleton<ICredentialsOrchestrator>(orchestrator);
        services.AddSingleton<FakeLoginSessionState>(loginState);
        services.AddSingleton<ILoginSessionState>(sp => sp.GetRequiredService<FakeLoginSessionState>());

        CapturingUsageSnapshotBroadcaster resolvedBroadcaster = broadcaster ?? new CapturingUsageSnapshotBroadcaster();
        services.AddSingleton<IUsageSnapshotBroadcaster>(resolvedBroadcaster);

        UsageSnapshotState resolvedState = state ?? new UsageSnapshotState();
        services.AddSingleton<IUsageSnapshotState>(resolvedState);
        services.AddSingleton<IUsageSnapshotStateMutator>(resolvedState);

        return services.BuildServiceProvider();
    }

    private static UsagePollCoordinator BuildCoordinator(
        ServiceProvider sp,
        ILogger<UsagePollCoordinator>? logger = null)
        => new(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<ICredentialsOrchestrator>(),
            sp.GetRequiredService<ILoginSessionState>(),
            sp.GetRequiredService<IUsageSnapshotState>(),
            sp.GetRequiredService<IUsageSnapshotStateMutator>(),
            sp.GetRequiredService<IUsageSnapshotBroadcaster>(),
            logger ?? NullLogger<UsagePollCoordinator>.Instance);

    private static async Task SeedOAuthAccountAsync(ServiceProvider sp, string email = "user@example.com")
    {
        await using AsyncServiceScope scope = sp.CreateAsyncScope();
        FoundryDbContext db = scope.ServiceProvider.GetRequiredService<FoundryDbContext>();
        ClaudeAccount account = ClaudeAccount.Create();
        account.RecordSuccessfulLogin(email, "Test Org", "pro");
        db.Set<ClaudeAccount>().Add(account);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static async Task SeedApiKeyAccountAsync(ServiceProvider sp)
    {
        await using AsyncServiceScope scope = sp.CreateAsyncScope();
        FoundryDbContext db = scope.ServiceProvider.GetRequiredService<FoundryDbContext>();
        ClaudeAccount account = ClaudeAccount.Create();
        db.Set<ClaudeAccount>().Add(account);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    // ---------------------------------------------------------------------------
    // Test doubles
    // ---------------------------------------------------------------------------

    private sealed class WarningCapturingLogger : ILogger<UsagePollCoordinator>
    {
        private readonly List<string> _warnings = [];

        public IReadOnlyList<string> Warnings
        {
            get
            {
                lock (_warnings)
                {
                    return [.. _warnings];
                }
            }
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                lock (_warnings)
                {
                    _warnings.Add(formatter(state, exception));
                }
            }
        }
    }

    internal sealed class CapturingUsageSnapshotBroadcaster : IUsageSnapshotBroadcaster
    {
        private readonly List<UsageSnapshot?> _sent = [];

        public IReadOnlyList<UsageSnapshot?> Sent => _sent;
        public UsageSnapshot? LastSent => _sent.Count > 0 ? _sent[^1] : null;

        public Task SendSnapshotAsync(UsageSnapshot? snapshot, CancellationToken cancellationToken)
        {
            _sent.Add(snapshot);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Counts <see cref="ICredentialsOrchestrator.RunUsageReportAsync"/> calls and blocks
    /// inside until the test releases the gate, allowing the single-flight JOIN test to
    /// reproduce a concurrent second caller.
    /// </summary>
    private sealed class CountingBlockingOrchestrator(Task gate, Action onCall) : ICredentialsOrchestrator
    {
        public TaskCompletionSource UsageStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<Result<string>> RunUsageReportAsync(
            UsageReportSpec spec,
            CancellationToken cancellationToken)
        {
            onCall();
            UsageStarted.TrySetResult();
            await gate;
            return Result<string>.Ok(ValidUsageLog);
        }

        // Unused members
        public Task<Result<string>> StartLoginContainerAsync(
            LoginContainerSpec spec,
            CancellationToken cancellationToken)
            => Task.FromResult(Result<string>.Ok(""));

        public Task<Result<string>> RunCreditProbeAsync(
            CreditProbeSpec spec,
            CancellationToken cancellationToken)
            => Task.FromResult(Result<string>.Ok(""));

        public Task DeliverLoginCodeAsync(
            string containerId,
            string code,
            CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<Result<AccountIdentity>> GetCredentialVolumeAuthStatusAsync(
            CancellationToken cancellationToken)
            => Task.FromResult(Result<AccountIdentity>.Fail(new Error("test", "not used")));

        public IAsyncEnumerable<string> StreamLogsAsync(
            string containerId,
            CancellationToken cancellationToken)
            => AsyncEnumerable.Empty<string>();

        public Task StopContainerAsync(string containerId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task RemoveContainerAsync(string containerId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<IReadOnlyList<string>> ListTransientContainersAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<string>>([]);

        public Task<IReadOnlyList<string>> ListExitedTransientContainersAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<string>>([]);

        public Task<Result<OAuthSessionStatus>> CheckOAuthSessionAsync(CancellationToken cancellationToken)
            => Task.FromResult(Result<OAuthSessionStatus>.Ok(new OAuthSessionStatus.Active()));

        public Task SeedOnboardingAsync(CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task EnsureCredentialVolumeAsync(CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<Result<string>> ReadOAuthAccessTokenAsync(CancellationToken cancellationToken)
            => Task.FromResult(Result<string>.Ok("token"));
    }

    // ---------------------------------------------------------------------------
    // Behavior b: Parsed → stores snapshot and broadcasts it
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task WhenParsed_StoresSnapshotAndBroadcastsIt()
    {
        // Arrange
        FakeCredentialsOrchestrator orchestrator = new FakeCredentialsOrchestrator()
            .WithUsageReportLogs(ValidUsageLog);
        CapturingUsageSnapshotBroadcaster broadcaster = new();

        ServiceProvider sp = BuildServiceProvider(
            _connection,
            orchestrator,
            new FakeLoginSessionState(),
            broadcaster);
        await using (sp)
        {
            await SeedOAuthAccountAsync(sp);
            UsagePollCoordinator sut = BuildCoordinator(sp);

            // Act
            UsageSnapshot? result = await sut.PollAsync(TestContext.Current.CancellationToken);

            // Assert — returned snapshot
            result.ShouldNotBeNull();
            result.Limits.ShouldNotBeEmpty();
            result.AccountEmail.ShouldBe("user@example.com");

            // Assert — state updated
            IUsageSnapshotState state = sp.GetRequiredService<IUsageSnapshotState>();
            state.Current.ShouldNotBeNull();
            state.Current.AccountEmail.ShouldBe("user@example.com");

            // Assert — broadcast sent once with the snapshot
            broadcaster.Sent.Count.ShouldBe(1);
            broadcaster.LastSent.ShouldNotBeNull();
            broadcaster.LastSent.AccountEmail.ShouldBe("user@example.com");
        }
    }

    [Fact]
    public async Task WhenParsed_ClearsLastRefreshFailedAtFromPriorSnapshot()
    {
        // Arrange: seed a snapshot with a failure stamp, then run a successful poll
        FakeCredentialsOrchestrator orchestrator = new FakeCredentialsOrchestrator()
            .WithUsageReportLogs(ValidUsageLog);

        UsageSnapshotState sharedState = new();
        CapturingUsageSnapshotBroadcaster broadcaster = new();

        ServiceProvider sp = BuildServiceProvider(
            _connection,
            orchestrator,
            new FakeLoginSessionState(),
            broadcaster,
            sharedState);
        await using (sp)
        {
            await SeedOAuthAccountAsync(sp);

            // Pre-stamp failure on the state
            UsageSnapshot priorSnapshot = new UsageSnapshotBuilder().Build();
            sharedState.SetSnapshot(priorSnapshot);
            sharedState.MarkRefreshFailed("user@example.com", DateTimeOffset.UtcNow.AddMinutes(-5));

            UsagePollCoordinator sut = BuildCoordinator(sp);

            // Act
            UsageSnapshot? result = await sut.PollAsync(TestContext.Current.CancellationToken);

            // Assert — failure stamp cleared on success
            result.ShouldNotBeNull();
            result.LastRefreshFailedAt.ShouldBeNull();

            IUsageSnapshotState state = sp.GetRequiredService<IUsageSnapshotState>();
            state.Current.ShouldNotBeNull();
            state.Current.LastRefreshFailedAt.ShouldBeNull();
        }
    }

    // ---------------------------------------------------------------------------
    // Behavior c: Orchestrator failure → MarkRefreshFailed + broadcast retained snapshot
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task WhenOrchestratorFails_MarksRefreshFailedAndBroadcastsRetainedSnapshot()
    {
        // Arrange: seed a prior snapshot, then fail the orchestrator
        FakeCredentialsOrchestrator orchestrator = new FakeCredentialsOrchestrator()
            .WithUsageReportFailure(new Error("Docker.UsageReportStartFailed", "container failed"));

        UsageSnapshotState sharedState = new();
        CapturingUsageSnapshotBroadcaster broadcaster = new();

        ServiceProvider sp = BuildServiceProvider(
            _connection,
            orchestrator,
            new FakeLoginSessionState(),
            broadcaster,
            sharedState);
        await using (sp)
        {
            await SeedOAuthAccountAsync(sp);

            // Seed a prior good snapshot
            UsageSnapshot prior = new UsageSnapshotBuilder().WithAccountEmail("user@example.com").Build();
            sharedState.SetSnapshot(prior);

            UsagePollCoordinator sut = BuildCoordinator(sp);

            // Act
            UsageSnapshot? result = await sut.PollAsync(TestContext.Current.CancellationToken);

            // Assert — returned snapshot is the retained (now stamped) one
            result.ShouldNotBeNull();
            result.LastRefreshFailedAt.ShouldNotBeNull();

            // Assert — state retains prior snapshot, stamped with failure
            IUsageSnapshotState state = sp.GetRequiredService<IUsageSnapshotState>();
            state.Current.ShouldNotBeNull();
            state.Current.LastRefreshFailedAt.ShouldNotBeNull();

            // Assert — broadcast sent with the retained (stamped) snapshot
            broadcaster.Sent.Count.ShouldBe(1);
            broadcaster.LastSent.ShouldNotBeNull();
            broadcaster.LastSent.LastRefreshFailedAt.ShouldNotBeNull();
        }
    }

    [Fact]
    public async Task WhenOrchestratorFails_WithNoPriorSnapshot_BroadcastsUnavailableSnapshot()
    {
        // Arrange: no prior snapshot, orchestrator fails
        FakeCredentialsOrchestrator orchestrator = new FakeCredentialsOrchestrator()
            .WithUsageReportFailure(new Error("Docker.UsageReportStartFailed", "container failed"));
        CapturingUsageSnapshotBroadcaster broadcaster = new();

        ServiceProvider sp = BuildServiceProvider(
            _connection,
            orchestrator,
            new FakeLoginSessionState(),
            broadcaster);
        await using (sp)
        {
            await SeedOAuthAccountAsync(sp);
            UsagePollCoordinator sut = BuildCoordinator(sp);

            // Act
            UsageSnapshot? result = await sut.PollAsync(TestContext.Current.CancellationToken);

            // Assert — an unavailable snapshot is returned (no figures, failure stamped)
            result.ShouldNotBeNull();
            result.ShouldSatisfyAllConditions(
                () => result.IsUnavailable.ShouldBeTrue(),
                () => result.CapturedAt.ShouldBeNull(),
                () => result.Limits.ShouldBeEmpty(),
                () => result.AccountEmail.ShouldBe("user@example.com"),
                () => result.LastRefreshFailedAt.ShouldNotBeNull());

            // Assert — broadcast sent with the unavailable snapshot, not null
            broadcaster.Sent.Count.ShouldBe(1);
            broadcaster.LastSent.ShouldNotBeNull();
            broadcaster.LastSent.IsUnavailable.ShouldBeTrue();
        }
    }

    [Fact]
    public async Task WhenUnparseable_WithNoPriorSnapshot_BroadcastsUnavailableSnapshot()
    {
        // Arrange: no prior snapshot, output unparseable
        FakeCredentialsOrchestrator orchestrator = new FakeCredentialsOrchestrator()
            .WithUsageReportLogs(MalformedJsonLog);
        CapturingUsageSnapshotBroadcaster broadcaster = new();

        ServiceProvider sp = BuildServiceProvider(
            _connection,
            orchestrator,
            new FakeLoginSessionState(),
            broadcaster);
        await using (sp)
        {
            await SeedOAuthAccountAsync(sp);
            UsagePollCoordinator sut = BuildCoordinator(sp);

            // Act
            UsageSnapshot? result = await sut.PollAsync(TestContext.Current.CancellationToken);

            // Assert
            result.ShouldNotBeNull();
            result.IsUnavailable.ShouldBeTrue();
            IUsageSnapshotState state = sp.GetRequiredService<IUsageSnapshotState>();
            state.Current.ShouldNotBeNull();
            state.Current.IsUnavailable.ShouldBeTrue();
            broadcaster.LastSent.ShouldNotBeNull();
            broadcaster.LastSent.IsUnavailable.ShouldBeTrue();
        }
    }

    [Fact]
    public async Task WhenUnavailable_ThenPollSucceeds_BroadcastsNormalSnapshot()
    {
        // Arrange: first poll fails before any success, the next succeeds
        FakeCredentialsOrchestrator orchestrator = new FakeCredentialsOrchestrator()
            .WithUsageReportLogs(ValidUsageLog);
        UsageSnapshotState sharedState = new();
        sharedState.MarkRefreshFailed("user@example.com", DateTimeOffset.UtcNow.AddMinutes(-1));
        CapturingUsageSnapshotBroadcaster broadcaster = new();

        ServiceProvider sp = BuildServiceProvider(
            _connection,
            orchestrator,
            new FakeLoginSessionState(),
            broadcaster,
            sharedState);
        await using (sp)
        {
            await SeedOAuthAccountAsync(sp);
            UsagePollCoordinator sut = BuildCoordinator(sp);

            // Act
            UsageSnapshot? result = await sut.PollAsync(TestContext.Current.CancellationToken);

            // Assert
            result.ShouldNotBeNull();
            result.ShouldSatisfyAllConditions(
                () => result.IsUnavailable.ShouldBeFalse(),
                () => result.CapturedAt.ShouldNotBeNull(),
                () => result.Limits.ShouldNotBeEmpty(),
                () => result.LastRefreshFailedAt.ShouldBeNull());
            broadcaster.LastSent.ShouldNotBeNull();
            broadcaster.LastSent.IsUnavailable.ShouldBeFalse();
        }
    }

    // ---------------------------------------------------------------------------
    // Behavior c: Unparseable → MarkRefreshFailed + broadcast retained snapshot
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task WhenUnparseable_MarksRefreshFailedAndBroadcastsRetainedSnapshot()
    {
        // Arrange: seed a prior snapshot, then return malformed JSON
        FakeCredentialsOrchestrator orchestrator = new FakeCredentialsOrchestrator()
            .WithUsageReportLogs(MalformedJsonLog);

        UsageSnapshotState sharedState = new();
        CapturingUsageSnapshotBroadcaster broadcaster = new();

        ServiceProvider sp = BuildServiceProvider(
            _connection,
            orchestrator,
            new FakeLoginSessionState(),
            broadcaster,
            sharedState);
        await using (sp)
        {
            await SeedOAuthAccountAsync(sp);

            UsageSnapshot prior = new UsageSnapshotBuilder().WithAccountEmail("user@example.com").Build();
            sharedState.SetSnapshot(prior);

            UsagePollCoordinator sut = BuildCoordinator(sp);

            // Act
            UsageSnapshot? result = await sut.PollAsync(TestContext.Current.CancellationToken);

            // Assert — failure stamp applied to the retained snapshot
            result.ShouldNotBeNull();
            result.LastRefreshFailedAt.ShouldNotBeNull();

            // Assert — broadcast sent once with retained + stamped snapshot
            broadcaster.Sent.Count.ShouldBe(1);
            broadcaster.LastSent.ShouldNotBeNull();
            broadcaster.LastSent.LastRefreshFailedAt.ShouldNotBeNull();
        }
    }

    [Fact]
    public async Task WhenUnparseable_LogsWarningWithRedactedOutputSample()
    {
        // Arrange: no stream-json at all, with a token-shaped secret in the output
        const string secretLog = "Error: credential sk-ant-oat01-SECRETVALUE123 rejected\nPlease run /login";
        FakeCredentialsOrchestrator orchestrator = new FakeCredentialsOrchestrator()
            .WithUsageReportLogs(secretLog);
        WarningCapturingLogger logger = new();

        ServiceProvider sp = BuildServiceProvider(_connection, orchestrator, new FakeLoginSessionState());
        await using (sp)
        {
            await SeedOAuthAccountAsync(sp);
            UsagePollCoordinator sut = BuildCoordinator(sp, logger);

            // Act
            await sut.PollAsync(TestContext.Current.CancellationToken);

            // Assert — one warning carrying a redacted sample of the output
            string warning = logger.Warnings.ShouldHaveSingleItem();
            warning.ShouldContain("Please run /login");
            warning.ShouldNotContain("SECRETVALUE123");
        }
    }

    [Fact]
    public void BuildOutputSample_WhenOutputIsLong_TruncatesToMaxLength()
    {
        // Arrange
        string output = new('x', UsagePollCoordinator.UnparseableSampleMaxLength * 3);

        // Act
        string sample = UsagePollCoordinator.BuildOutputSample(output);

        // Assert — capped sample plus a single ellipsis marker
        sample.Length.ShouldBe(UsagePollCoordinator.UnparseableSampleMaxLength + 1);
    }

    [Fact]
    public void BuildOutputSample_WhenOutputIsEmpty_ReturnsEmptyMarker()
    {
        // Act
        string sample = UsagePollCoordinator.BuildOutputSample("  \n ");

        // Assert
        sample.ShouldBe("<empty>");
    }

    // ---------------------------------------------------------------------------
    // Behavior d: NoUsageReport → Clear + broadcast null
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task WhenNoUsageReport_ClearsSnapshotAndBroadcastsNull()
    {
        // Arrange: seed a prior snapshot, then return log with no usage_report
        FakeCredentialsOrchestrator orchestrator = new FakeCredentialsOrchestrator()
            .WithUsageReportLogs(NoUsageReportLog);

        UsageSnapshotState sharedState = new();
        CapturingUsageSnapshotBroadcaster broadcaster = new();

        ServiceProvider sp = BuildServiceProvider(
            _connection,
            orchestrator,
            new FakeLoginSessionState(),
            broadcaster,
            sharedState);
        await using (sp)
        {
            await SeedOAuthAccountAsync(sp);

            UsageSnapshot prior = new UsageSnapshotBuilder().WithAccountEmail("user@example.com").Build();
            sharedState.SetSnapshot(prior);

            UsagePollCoordinator sut = BuildCoordinator(sp);

            // Act
            UsageSnapshot? result = await sut.PollAsync(TestContext.Current.CancellationToken);

            // Assert — null returned (snapshot cleared)
            result.ShouldBeNull();

            // Assert — state cleared
            IUsageSnapshotState state = sp.GetRequiredService<IUsageSnapshotState>();
            state.Current.ShouldBeNull();

            // Assert — broadcast null
            broadcaster.Sent.Count.ShouldBe(1);
            broadcaster.LastSent.ShouldBeNull();
        }
    }

    // ---------------------------------------------------------------------------
    // Behavior g: Infrastructure fault — fault is logged and _inFlightTask is cleared
    // ---------------------------------------------------------------------------

    /// <summary>
    /// An orchestrator that throws an infrastructure exception from RunUsageReportAsync,
    /// simulating e.g. a Docker socket failure or transient infrastructure error that
    /// propagates before the handled Result paths can run.
    /// </summary>
    private sealed class ThrowingOrchestrator(Exception exception) : ICredentialsOrchestrator
    {
        public Task<Result<string>> RunUsageReportAsync(
            UsageReportSpec spec,
            CancellationToken cancellationToken)
            => Task.FromException<Result<string>>(exception);

        public Task<Result<string>> StartLoginContainerAsync(
            LoginContainerSpec spec,
            CancellationToken cancellationToken)
            => Task.FromResult(Result<string>.Ok(""));

        public Task<Result<string>> RunCreditProbeAsync(
            CreditProbeSpec spec,
            CancellationToken cancellationToken)
            => Task.FromResult(Result<string>.Ok(""));

        public Task DeliverLoginCodeAsync(
            string containerId,
            string code,
            CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<Result<AccountIdentity>> GetCredentialVolumeAuthStatusAsync(
            CancellationToken cancellationToken)
            => Task.FromResult(Result<AccountIdentity>.Fail(new Error("test", "not used")));

        public IAsyncEnumerable<string> StreamLogsAsync(
            string containerId,
            CancellationToken cancellationToken)
            => AsyncEnumerable.Empty<string>();

        public Task StopContainerAsync(string containerId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task RemoveContainerAsync(string containerId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<IReadOnlyList<string>> ListTransientContainersAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<string>>([]);

        public Task<IReadOnlyList<string>> ListExitedTransientContainersAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<string>>([]);

        public Task<Result<OAuthSessionStatus>> CheckOAuthSessionAsync(CancellationToken cancellationToken)
            => Task.FromResult(Result<OAuthSessionStatus>.Ok(new OAuthSessionStatus.Active()));

        public Task SeedOnboardingAsync(CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task EnsureCredentialVolumeAsync(CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<Result<string>> ReadOAuthAccessTokenAsync(CancellationToken cancellationToken)
            => Task.FromResult(Result<string>.Ok("token"));
    }

    [Fact]
    public async Task WhenInfrastructureFaultOccurs_LogsErrorAndClearsInFlightTaskSoSubsequentPollCanRun()
    {
        // Arrange
        InvalidOperationException infrastructureException = new("Docker socket unavailable.");
        ThrowingOrchestrator throwingOrchestrator = new(infrastructureException);
        CapturingLogger<UsagePollCoordinator> capturingLogger = new();

        ServiceProvider sp = BuildServiceProvider(
            _connection,
            throwingOrchestrator,
            new FakeLoginSessionState());
        await using (sp)
        {
            await SeedOAuthAccountAsync(sp);

            UsagePollCoordinator sut = new(
                sp.GetRequiredService<IServiceScopeFactory>(),
                throwingOrchestrator,
                sp.GetRequiredService<ILoginSessionState>(),
                sp.GetRequiredService<IUsageSnapshotState>(),
                sp.GetRequiredService<IUsageSnapshotStateMutator>(),
                sp.GetRequiredService<IUsageSnapshotBroadcaster>(),
                capturingLogger);

            // Act — the task returned by PollAsync faults because the orchestrator throws
            Task<UsageSnapshot?> firstPoll = sut.PollAsync(TestContext.Current.CancellationToken);
            await Should.ThrowAsync<InvalidOperationException>(() => firstPoll);

            // Allow the ContinueWith cleanup continuation to execute
            await Task.Delay(50, TestContext.Current.CancellationToken);

            // Assert — infrastructure fault was logged via LogError with the original exception
            capturingLogger.Entries.ShouldContain(e =>
                e.Level == LogLevel.Error && e.Exception == infrastructureException);

            // Assert — _inFlightTask is cleared: a subsequent PollAsync starts a new poll
            // (returns a new, distinct task object — not the same faulted reference)
            Task<UsageSnapshot?> secondPoll = sut.PollAsync(TestContext.Current.CancellationToken);
            await Should.ThrowAsync<InvalidOperationException>(() => secondPoll);

            secondPoll.ShouldNotBeSameAs(firstPoll);
        }
    }

    // ---------------------------------------------------------------------------
    // Behavior e: Single-flight JOIN — only one RunUsageReportAsync call for two concurrent PollAsync
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task WhenSecondConcurrentCaller_JoinsInFlightPollAndNoSecondContainerStarts()
    {
        // Arrange
        await SeedOAuthAccountAsync(_serviceProvider);

        TaskCompletionSource gateTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int usageReportCallCount = 0;
        CountingBlockingOrchestrator countingOrchestrator = new(
            gateTcs.Task,
            () => usageReportCallCount++);

        CapturingUsageSnapshotBroadcaster broadcaster = new();

        ServiceProvider sp = BuildServiceProvider(
            _connection,
            countingOrchestrator,
            new FakeLoginSessionState(),
            broadcaster);
        await using (sp)
        {
            UsagePollCoordinator sut = BuildCoordinator(sp);

            // Start first poll — blocks inside RunUsageReportAsync
            Task<UsageSnapshot?> firstPoll = sut.PollAsync(TestContext.Current.CancellationToken);

            // Wait until the first poll is inside the orchestrator
            await countingOrchestrator.UsageStarted.Task;

            // Act — second poll while first is in flight (JOIN)
            Task<UsageSnapshot?> secondPoll = sut.PollAsync(TestContext.Current.CancellationToken);

            // Release the gate so both can complete
            gateTcs.SetResult();
            UsageSnapshot?[] results = await Task.WhenAll(firstPoll, secondPoll);

            // Assert — exactly one RunUsageReportAsync call (JOIN semantics)
            usageReportCallCount.ShouldBe(1);

            // Assert — both callers received a non-null snapshot
            results[0].ShouldNotBeNull();
            results[1].ShouldNotBeNull();
        }
    }

    // ---------------------------------------------------------------------------
    // Behavior f: Skip when CanReportUsage is false (API-key mode)
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task WhenCanReportUsageIsFalse_SkipsPollAndLeavesStateUntouched()
    {
        // Arrange: API-key account — CanReportUsage is false
        FakeCredentialsOrchestrator orchestrator = new FakeCredentialsOrchestrator()
            .WithUsageReportLogs(ValidUsageLog);
        CapturingUsageSnapshotBroadcaster broadcaster = new();

        ServiceProvider sp = BuildServiceProvider(
            _connection,
            orchestrator,
            new FakeLoginSessionState(),
            broadcaster);
        await using (sp)
        {
            await SeedApiKeyAccountAsync(sp);
            UsagePollCoordinator sut = BuildCoordinator(sp);

            // Act
            UsageSnapshot? result = await sut.PollAsync(TestContext.Current.CancellationToken);

            // Assert — null returned (skipped)
            result.ShouldBeNull();

            // Assert — state untouched
            IUsageSnapshotState state = sp.GetRequiredService<IUsageSnapshotState>();
            state.Current.ShouldBeNull();

            // Assert — no broadcast
            broadcaster.Sent.ShouldBeEmpty();
        }
    }

    // ---------------------------------------------------------------------------
    // Behavior f: Skip when IsLoginActive is true
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task WhenLoginActive_SkipsPollAndLeavesStateUntouched()
    {
        // Arrange
        FakeCredentialsOrchestrator orchestrator = new FakeCredentialsOrchestrator()
            .WithUsageReportLogs(ValidUsageLog);
        FakeLoginSessionState loginState = new(isLoginActive: true);
        CapturingUsageSnapshotBroadcaster broadcaster = new();
        UsageSnapshotState sharedState = new();

        ServiceProvider sp = BuildServiceProvider(
            _connection,
            orchestrator,
            loginState,
            broadcaster,
            sharedState);
        await using (sp)
        {
            await SeedOAuthAccountAsync(sp);

            // Seed a snapshot to prove it's untouched
            UsageSnapshot prior = new UsageSnapshotBuilder().WithAccountEmail("user@example.com").Build();
            sharedState.SetSnapshot(prior);

            UsagePollCoordinator sut = BuildCoordinator(sp);

            // Act
            UsageSnapshot? result = await sut.PollAsync(TestContext.Current.CancellationToken);

            // Assert — null returned (skipped — login suppresses the poll)
            result.ShouldBeNull();

            // Assert — state untouched (prior snapshot still present)
            IUsageSnapshotState state = sp.GetRequiredService<IUsageSnapshotState>();
            state.Current.ShouldNotBeNull();
            state.Current.AccountEmail.ShouldBe("user@example.com");

            // Assert — no broadcast
            broadcaster.Sent.ShouldBeEmpty();
        }
    }

    // ---------------------------------------------------------------------------
    // Two-source poll: /usage first, probe turn as fallback (ADR 0097)
    // ---------------------------------------------------------------------------

    private async Task<(UsageSnapshot? Result, CapturingUsageSnapshotBroadcaster Broadcaster, UsageSnapshotState State)> RunPollAsync(
        FakeCredentialsOrchestrator orchestrator,
        UsageSnapshot? prior = null,
        ILogger<UsagePollCoordinator>? logger = null)
    {
        UsageSnapshotState sharedState = new();
        CapturingUsageSnapshotBroadcaster broadcaster = new();

        ServiceProvider sp = BuildServiceProvider(
            _connection,
            orchestrator,
            new FakeLoginSessionState(),
            broadcaster,
            sharedState);
        await using (sp)
        {
            await SeedOAuthAccountAsync(sp);

            if (prior is not null)
            {
                sharedState.SetSnapshot(prior);
            }

            UsagePollCoordinator sut = BuildCoordinator(sp, logger);
            UsageSnapshot? result = await sut.PollAsync(TestContext.Current.CancellationToken);
            return (result, broadcaster, sharedState);
        }
    }

    [Fact]
    public async Task WhenUsageReportParsed_RunsOnlyTheUsageCommandAndStoresUsageReportSnapshot()
    {
        // Arrange
        FakeCredentialsOrchestrator orchestrator = new FakeCredentialsOrchestrator()
            .WithUsageReportLogs(ValidUsageLog)
            .WithProbeTurnLogs(ProbeTurnLog);

        // Act
        (UsageSnapshot? result, _, _) = await RunPollAsync(orchestrator);

        // Assert
        orchestrator.UsageReportCommands.ShouldBe([UsageReportCommand.UsageCommand]);
        UsageSnapshot snapshot = result.ShouldNotBeNull();
        snapshot.ShouldSatisfyAllConditions(
            () => snapshot.Source.ShouldBe(UsageSnapshotSource.UsageReport),
            () => snapshot.ExtraUsage.ShouldNotBeNull().UsedCredits.ShouldBe(16568),
            () => snapshot.Limits.Select(l => l.Severity).ShouldBe(["normal", "normal"]));
    }

    [Fact]
    public async Task WhenRateLimitsMissing_RunsProbeTurnAfterUsageCommandAndKeepsCredits()
    {
        // Arrange
        FakeCredentialsOrchestrator orchestrator = new FakeCredentialsOrchestrator()
            .WithUsageReportLogs(RateLimitsMissingLog)
            .WithProbeTurnLogs(ProbeTurnLog);

        // Act
        (UsageSnapshot? result, CapturingUsageSnapshotBroadcaster broadcaster, UsageSnapshotState state) =
            await RunPollAsync(orchestrator);

        // Assert — sequential: /usage, then the probe turn, within the one poll
        orchestrator.UsageReportCommands.ShouldBe([UsageReportCommand.UsageCommand, UsageReportCommand.ProbeTurn]);
        UsageSnapshot snapshot = result.ShouldNotBeNull();
        snapshot.ShouldSatisfyAllConditions(
            () => snapshot.Source.ShouldBe(UsageSnapshotSource.ProbeTurn),
            () => snapshot.Limits.Select(l => l.Kind).ShouldBe(["session", "weekly_all"]),
            () => snapshot.ExtraUsage.ShouldNotBeNull().MonthlyLimit.ShouldBe(50000),
            () => snapshot.LastRefreshFailedAt.ShouldBeNull());
        ((IUsageSnapshotState)state).Current.ShouldBe(snapshot);
        broadcaster.Sent.ShouldHaveSingleItem().ShouldBe(snapshot);
    }

    [Fact]
    public async Task WhenRateLimitsMissing_LogsWarningNamingTheCliVersion()
    {
        // Arrange
        FakeCredentialsOrchestrator orchestrator = new FakeCredentialsOrchestrator()
            .WithUsageReportLogs(RateLimitsMissingLog)
            .WithProbeTurnLogs(ProbeTurnLog);
        WarningCapturingLogger logger = new();

        // Act
        await RunPollAsync(orchestrator, logger: logger);

        // Assert
        logger.Warnings.ShouldContain(w => w.Contains("2.1.399") && w.Contains("probe turn"));
    }

    [Fact]
    public async Task WhenRateLimitsMissingAndProbeTurnFails_IsAFailedPollRetainingLastSnapshot()
    {
        // Arrange
        FakeCredentialsOrchestrator orchestrator = new FakeCredentialsOrchestrator()
            .WithUsageReportLogs(RateLimitsMissingLog)
            .WithProbeTurnFailure(new Error("Docker.UsageReportStartFailed", "container failed"));
        UsageSnapshot prior = new UsageSnapshotBuilder().Build();

        // Act
        (UsageSnapshot? result, _, _) = await RunPollAsync(orchestrator, prior);

        // Assert
        UsageSnapshot snapshot = result.ShouldNotBeNull();
        snapshot.ShouldSatisfyAllConditions(
            () => snapshot.Limits.ShouldBe(prior.Limits),
            () => snapshot.Source.ShouldBe(prior.Source),
            () => snapshot.LastRefreshFailedAt.ShouldNotBeNull());
    }

    [Theory]
    [InlineData(nameof(MalformedJsonLog))]
    [InlineData(nameof(NoUsageReportLog))]
    public async Task WhenRateLimitsMissingAndProbeTurnHasNoWindows_IsAFailedPoll(string probeLogName)
    {
        // Arrange — /usage proved the account reportable, so a probe with no windows is a failure, not a clear
        string probeLog = probeLogName == nameof(MalformedJsonLog) ? MalformedJsonLog : NoUsageReportLog;
        FakeCredentialsOrchestrator orchestrator = new FakeCredentialsOrchestrator()
            .WithUsageReportLogs(RateLimitsMissingLog)
            .WithProbeTurnLogs(probeLog);
        UsageSnapshot prior = new UsageSnapshotBuilder().Build();

        // Act
        (UsageSnapshot? result, _, _) = await RunPollAsync(orchestrator, prior);

        // Assert
        UsageSnapshot snapshot = result.ShouldNotBeNull();
        snapshot.ShouldSatisfyAllConditions(
            () => snapshot.Limits.ShouldBe(prior.Limits),
            () => snapshot.LastRefreshFailedAt.ShouldNotBeNull());
    }

    [Theory]
    [InlineData(nameof(MalformedJsonLog))]
    [InlineData(nameof(NoUsageReportLog))]
    [InlineData("failure")]
    public async Task WhenUsageCommandDoesNotReportMissingRateLimits_RunsNoFallback(string outcome)
    {
        // Arrange
        FakeCredentialsOrchestrator orchestrator = new FakeCredentialsOrchestrator()
            .WithProbeTurnLogs(ProbeTurnLog);
        _ = outcome switch
        {
            nameof(MalformedJsonLog) => orchestrator.WithUsageReportLogs(MalformedJsonLog),
            nameof(NoUsageReportLog) => orchestrator.WithUsageReportLogs(NoUsageReportLog),
            _ => orchestrator.WithUsageReportFailure(new Error("Docker.UsageReportStartFailed", "container failed")),
        };

        // Act
        await RunPollAsync(orchestrator);

        // Assert
        orchestrator.UsageReportCommands.ShouldBe([UsageReportCommand.UsageCommand]);
    }
}
