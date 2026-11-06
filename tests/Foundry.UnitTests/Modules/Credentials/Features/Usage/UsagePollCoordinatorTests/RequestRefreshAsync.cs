using Foundry.Modules.Credentials.Domain.Entities;
using Foundry.Modules.Credentials.Features.Login;
using Foundry.Modules.Credentials.Features.Usage;
using Foundry.Modules.Credentials.Infrastructure.Orchestration;
using Foundry.Testing;
using Foundry.UnitTests.Fakes.Credentials;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Credentials.Features.Usage.UsagePollCoordinatorTests;

public sealed class RequestRefreshAsync : IAsyncDisposable
{
    /// <summary>Minimal <c>/usage</c> stream-json carrying a usage_report with rate limits — a successful parse.</summary>
    private const string ValidUsageLog = """
        {"type":"system","subtype":"init","cwd":"/workspace","session_id":"abc123","tools":[],"mcp_servers":[],"model":"claude-opus-4-8","permissionMode":"default","apiKeySource":"none","claude_code_version":"2.1.316"}
        {"type":"assistant","message":{"id":"msg_01","type":"message","role":"assistant","model":"<synthetic>","content":[{"type":"text","text":"Current session"}]},"parent_tool_use_id":null,"session_id":"abc123","usage_report":{"session":{},"rate_limits":{"limits":[{"kind":"session","group":"session","percent":14,"resets_at":"2026-11-06T13:00:00Z","severity":"normal","is_active":true}],"extra_usage":null}}}
        {"type":"result","subtype":"success","is_error":false,"duration_ms":100,"duration_api_ms":0,"num_turns":0,"result":"","total_cost_usd":0,"session_id":"abc123"}
        """;

    private readonly SqliteConnection _connection;
    private readonly FakeCredentialsOrchestrator _orchestrator = new FakeCredentialsOrchestrator()
        .WithUsageReportLogs(ValidUsageLog);
    private readonly FakeLoginSessionState _loginState = new();
    private readonly SignallingBroadcaster _broadcaster = new();
    private readonly UsageSnapshotState _state = new();
    private readonly ServiceProvider _serviceProvider;

    public RequestRefreshAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        ServiceCollection services = new();
        services.AddDbContext<FoundryDbContext>((_, options) => options.UseSqlite(_connection));
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<FoundryDbContext>());
        _serviceProvider = services.BuildServiceProvider();

        using IServiceScope setup = _serviceProvider.CreateScope();
        setup.ServiceProvider.GetRequiredService<FoundryDbContext>().Database.EnsureCreated();
    }

    public async ValueTask DisposeAsync()
    {
        await _serviceProvider.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private UsagePollCoordinator BuildCoordinator(ICredentialsOrchestrator? orchestrator = null)
        => new(
            _serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            orchestrator ?? _orchestrator,
            _loginState,
            _state,
            _state,
            _broadcaster,
            NullLogger<UsagePollCoordinator>.Instance);

    private async Task SeedAccountAsync(bool oauth = true)
    {
        await using AsyncServiceScope scope = _serviceProvider.CreateAsyncScope();
        FoundryDbContext db = scope.ServiceProvider.GetRequiredService<FoundryDbContext>();
        ClaudeAccount account = ClaudeAccount.Create();
        if (oauth)
        {
            account.RecordSuccessfulLogin("user@example.com", "Test Org", "pro");
        }

        db.Set<ClaudeAccount>().Add(account);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private sealed class SignallingBroadcaster : IUsageSnapshotBroadcaster
    {
        public TaskCompletionSource<UsageSnapshot?> FirstSent { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task SendSnapshotAsync(UsageSnapshot? snapshot, CancellationToken cancellationToken)
        {
            FirstSent.TrySetResult(snapshot);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task WhenNoAccount_ReturnsUnavailableAndStartsNothing()
    {
        // Arrange
        UsagePollCoordinator sut = BuildCoordinator();

        // Act
        UsageRefreshOutcome outcome = await sut.RequestRefreshAsync(TestContext.Current.CancellationToken);

        // Assert
        outcome.ShouldBe(UsageRefreshOutcome.Unavailable);
        _orchestrator.UsageReportCallCount.ShouldBe(0);
    }

    [Fact]
    public async Task WhenCanReportUsageIsFalse_ReturnsUnavailableAndStartsNothing()
    {
        // Arrange — API-key account cannot report usage
        await SeedAccountAsync(oauth: false);
        UsagePollCoordinator sut = BuildCoordinator();

        // Act
        UsageRefreshOutcome outcome = await sut.RequestRefreshAsync(TestContext.Current.CancellationToken);

        // Assert
        outcome.ShouldBe(UsageRefreshOutcome.Unavailable);
        _orchestrator.UsageReportCallCount.ShouldBe(0);
    }

    [Fact]
    public async Task WhenLoginActive_ReturnsLoginActiveAndStartsNothing()
    {
        // Arrange
        await SeedAccountAsync();
        _loginState.IsLoginActive = true;
        UsagePollCoordinator sut = BuildCoordinator();

        // Act
        UsageRefreshOutcome outcome = await sut.RequestRefreshAsync(TestContext.Current.CancellationToken);

        // Assert
        outcome.ShouldBe(UsageRefreshOutcome.LoginActive);
        _orchestrator.UsageReportCallCount.ShouldBe(0);
    }

    [Fact]
    public async Task WhenNoPollInFlight_StartsPollWithoutWaitingAndBroadcastsResult()
    {
        // Arrange — the usage report blocks until released
        await SeedAccountAsync();
        FakeCredentialsOrchestrator orchestrator = new FakeCredentialsOrchestrator()
            .WithUsageReportLogs(ValidUsageLog)
            .WithBlockingUsageReport();
        UsagePollCoordinator sut = BuildCoordinator(orchestrator);

        // Act — returns while the container is still running
        UsageRefreshOutcome outcome = await sut.RequestRefreshAsync(TestContext.Current.CancellationToken);

        // Assert — started, not yet broadcast
        outcome.ShouldBe(UsageRefreshOutcome.Started);
        await orchestrator.UsageReportStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        _broadcaster.FirstSent.Task.IsCompleted.ShouldBeFalse();

        // Assert — the result reaches clients via the push once the poll completes
        orchestrator.ReleaseUsageReport();
        UsageSnapshot? pushed = await _broadcaster.FirstSent.Task.WaitAsync(
            TimeSpan.FromSeconds(10),
            TestContext.Current.CancellationToken);
        pushed.ShouldNotBeNull();
        orchestrator.UsageReportCallCount.ShouldBe(1);
    }

    [Fact]
    public async Task WhenPollInFlight_ReturnsInFlightAndStartsNoSecondPoll()
    {
        // Arrange — a background tick is in flight
        await SeedAccountAsync();
        FakeCredentialsOrchestrator orchestrator = new FakeCredentialsOrchestrator()
            .WithUsageReportLogs(ValidUsageLog)
            .WithBlockingUsageReport();
        UsagePollCoordinator sut = BuildCoordinator(orchestrator);
        Task<UsageSnapshot?> tick = sut.PollAsync(TestContext.Current.CancellationToken);
        await orchestrator.UsageReportStarted.Task.WaitAsync(TestContext.Current.CancellationToken);

        // Act — repeated clicks while the poll runs
        UsageRefreshOutcome first = await sut.RequestRefreshAsync(TestContext.Current.CancellationToken);
        UsageRefreshOutcome second = await sut.RequestRefreshAsync(TestContext.Current.CancellationToken);

        // Assert
        first.ShouldBe(UsageRefreshOutcome.InFlight);
        second.ShouldBe(UsageRefreshOutcome.InFlight);

        orchestrator.ReleaseUsageReport();
        await tick;
        orchestrator.UsageReportCallCount.ShouldBe(1);
    }

    [Fact]
    public async Task WhenLastSuccessfulSnapshotIsYoungerThanFloor_ReturnsAlreadyCurrentAndStartsNothing()
    {
        // Arrange
        await SeedAccountAsync();
        _state.SetSnapshot(new UsageSnapshotBuilder()
            .WithCapturedAt(DateTimeOffset.UtcNow.AddSeconds(-5))
            .Build());
        UsagePollCoordinator sut = BuildCoordinator();

        // Act
        UsageRefreshOutcome outcome = await sut.RequestRefreshAsync(TestContext.Current.CancellationToken);

        // Assert
        outcome.ShouldBe(UsageRefreshOutcome.AlreadyCurrent);
        _orchestrator.UsageReportCallCount.ShouldBe(0);
    }

    [Fact]
    public async Task WhenLastSuccessfulSnapshotIsOlderThanFloor_StartsPoll()
    {
        // Arrange
        await SeedAccountAsync();
        _state.SetSnapshot(new UsageSnapshotBuilder()
            .WithCapturedAt(DateTimeOffset.UtcNow - UsagePollCoordinator.RefreshFloor - TimeSpan.FromSeconds(1))
            .Build());
        UsagePollCoordinator sut = BuildCoordinator();

        // Act
        UsageRefreshOutcome outcome = await sut.RequestRefreshAsync(TestContext.Current.CancellationToken);

        // Assert
        outcome.ShouldBe(UsageRefreshOutcome.Started);
        await _broadcaster.FirstSent.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        _orchestrator.UsageReportCallCount.ShouldBe(1);
    }

    [Fact]
    public async Task WhenLastPollFailed_FloorDoesNotApply()
    {
        // Arrange — a fresh snapshot whose latest refresh attempt failed
        await SeedAccountAsync();
        _state.SetSnapshot(new UsageSnapshotBuilder()
            .WithCapturedAt(DateTimeOffset.UtcNow.AddSeconds(-5))
            .Build());
        _state.MarkRefreshFailed("user@example.com", DateTimeOffset.UtcNow);
        UsagePollCoordinator sut = BuildCoordinator();

        // Act
        UsageRefreshOutcome outcome = await sut.RequestRefreshAsync(TestContext.Current.CancellationToken);

        // Assert
        outcome.ShouldBe(UsageRefreshOutcome.Started);
        await _broadcaster.FirstSent.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        _orchestrator.UsageReportCallCount.ShouldBe(1);
    }

    [Fact]
    public async Task WhenRequestedAgainRightAfterSuccessfulRefresh_ReturnsAlreadyCurrent()
    {
        // Arrange — a first refresh completes successfully
        await SeedAccountAsync();
        UsagePollCoordinator sut = BuildCoordinator();
        UsageRefreshOutcome first = await sut.RequestRefreshAsync(TestContext.Current.CancellationToken);
        await _broadcaster.FirstSent.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        // Act — another tab clicks right after (the in-flight reference may still be clearing)
        UsageRefreshOutcome second = await sut.RequestRefreshAsync(TestContext.Current.CancellationToken);

        // Assert — at most one poll per floor window after a success
        first.ShouldBe(UsageRefreshOutcome.Started);
        second.ShouldBeOneOf(UsageRefreshOutcome.AlreadyCurrent, UsageRefreshOutcome.InFlight);
        _orchestrator.UsageReportCallCount.ShouldBe(1);
    }
}
