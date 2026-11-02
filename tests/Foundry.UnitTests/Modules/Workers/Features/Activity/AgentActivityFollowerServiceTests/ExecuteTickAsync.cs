using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Activity;
using Foundry.Shared;
using Foundry.UnitTests.Fakes.Workers;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Activity.AgentActivityFollowerServiceTests;

public sealed class ExecuteTickAsync : IAsyncDisposable
{
    private const string BashToolUse =
        """2026-11-02T10:00:00.000000000Z {"type":"assistant","message":{"content":[{"type":"tool_use","id":"t1","name":"Bash","input":{"command":"dotnet test"}}]}}""";

    private static readonly TimeSpan ReconcileInterval = TimeSpan.FromSeconds(10);

    private readonly SqliteConnection _connection;
    private readonly AgentActivityStore _store = new();
    private readonly SpyAgentActivityBroadcaster _broadcaster = new();
    private readonly MutableTimeProvider _time = new(new DateTimeOffset(2026, 11, 2, 12, 0, 0, TimeSpan.Zero));

    public ExecuteTickAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        using FoundryDbContext setup = CreateDbContext();
        setup.Database.EnsureCreated();
    }

    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task WhenRunIsActive_FollowsItsLogAndBroadcastsTheProjection()
    {
        // Arrange
        ActiveRun run = SeedActiveRun();
        FakeWorkerOrchestrator orchestrator = new FakeWorkerOrchestrator().WithLogLines(BashToolUse);
        AgentActivityFollowerService sut = BuildService(orchestrator);

        // Act
        await TickUntilAsync(sut, () => _store.GetCurrent().Count > 0);

        // Assert
        AgentActivity activity = _store.GetCurrent().ShouldHaveSingleItem();
        activity.WorkerRunId.ShouldBe(run.Id);
        activity.IssueId.ShouldBe(run.IssueId.Value);
        activity.InFlightTool.ShouldNotBeNull().Name.ShouldBe("Bash");
        _broadcaster.Activities.ShouldContain(activity);

        await sut.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task WhenProjectionIsUnchanged_DoesNotBroadcastAgain()
    {
        // Arrange
        SeedActiveRun();
        FakeWorkerOrchestrator orchestrator = new FakeWorkerOrchestrator().WithLogLines(BashToolUse);
        AgentActivityFollowerService sut = BuildService(orchestrator);
        await TickUntilAsync(sut, () => _broadcaster.Activities.Count > 0);

        // Act
        await sut.ExecuteTickAsync(CancellationToken.None);

        // Assert
        _broadcaster.Activities.Count.ShouldBe(1);

        await sut.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task WhenRunLeavesActiveRun_DisposesProjectionAndBroadcastsEnded()
    {
        // Arrange
        ActiveRun run = SeedActiveRun();
        FakeWorkerOrchestrator orchestrator = new FakeWorkerOrchestrator().WithLogLines(BashToolUse);
        AgentActivityFollowerService sut = BuildService(orchestrator);
        await TickUntilAsync(sut, () => _store.GetCurrent().Count > 0);

        await using (FoundryDbContext db = CreateDbContext())
        {
            WorkerRun stored = await db.Set<WorkerRun>().SingleAsync(r => r.Id == run.Id, TestContext.Current.CancellationToken);
            db.Set<WorkerRun>().Remove(stored);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        _time.Advance(ReconcileInterval);

        // Act
        await sut.ExecuteTickAsync(CancellationToken.None);

        // Assert
        _store.GetCurrent().ShouldBeEmpty();
        _broadcaster.Ended.ShouldHaveSingleItem().ShouldBe(new AgentActivityEnded(run.Id, run.IssueId.Value));
    }

    [Fact]
    public async Task WhenLogStreamEndsNormally_DoesNotReattach()
    {
        // Arrange
        SeedActiveRun();
        FakeWorkerOrchestrator orchestrator = new FakeWorkerOrchestrator().WithLogLines(BashToolUse);
        AgentActivityFollowerService sut = BuildService(orchestrator);
        await TickUntilAsync(sut, () => _store.GetCurrent().Count > 0);

        _time.Advance(ReconcileInterval);

        // Act
        await sut.ExecuteTickAsync(CancellationToken.None);

        // Assert
        orchestrator.StreamLogsCallCount.ShouldBe(1);

        await sut.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task WhenLogStreamFaults_ReattachesFromTheStartOnNextReconcile()
    {
        // Arrange
        SeedActiveRun();
        FakeWorkerOrchestrator orchestrator = new FakeWorkerOrchestrator()
            .WithLogLines(BashToolUse)
            .WithLogStreamFault(new IOException("daemon went away"));
        AgentActivityFollowerService sut = BuildService(orchestrator);

        // Act — each tick crosses the reconcile interval, so a faulted follower is re-attached.
        await TickUntilAsync(sut, () => orchestrator.StreamLogsCallCount >= 2, advanceEachTick: ReconcileInterval);

        // Assert
        orchestrator.StreamLogsCallCount.ShouldBeGreaterThanOrEqualTo(2);

        await sut.StopAsync(CancellationToken.None);
    }

    private async Task TickUntilAsync(
        AgentActivityFollowerService sut,
        Func<bool> condition,
        TimeSpan? advanceEachTick = null)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);

        while (DateTime.UtcNow < deadline)
        {
            _time.Advance(advanceEachTick ?? TimeSpan.Zero);
            await sut.ExecuteTickAsync(CancellationToken.None);

            if (condition())
            {
                return;
            }

            await Task.Delay(10);
        }

        throw new TimeoutException("Condition was not met within 5 seconds.");
    }

    private ActiveRun SeedActiveRun()
    {
        using FoundryDbContext db = CreateDbContext();
        StartingRun starting = StartingRun.Begin(IssueId.New(), WorkerRunId.New());
        ActiveRun activeRun = starting.Activate(
            ContainerId.From("container-123"),
            BranchName.From("feat/1-default"),
            MonitoredRepositoryId.New());
        db.Set<WorkerRun>().Add(activeRun);
        db.SaveChanges();
        return activeRun;
    }

    private FoundryDbContext CreateDbContext()
    {
        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .Options;
        return new FoundryDbContext(options);
    }

    private AgentActivityFollowerService BuildService(FakeWorkerOrchestrator orchestrator)
    {
        ServiceCollection services = new();
        services.AddScoped<DbContext>(_ => CreateDbContext());
        ServiceProvider provider = services.BuildServiceProvider();

        return new AgentActivityFollowerService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            orchestrator,
            _broadcaster,
            _store,
            _time,
            NullLogger<AgentActivityFollowerService>.Instance);
    }

    private sealed class SpyAgentActivityBroadcaster : IAgentActivityBroadcaster
    {
        public List<AgentActivity> Activities { get; } = [];

        public List<AgentActivityEnded> Ended { get; } = [];

        public Task BroadcastActivityAsync(AgentActivity activity, CancellationToken cancellationToken)
        {
            Activities.Add(activity);
            return Task.CompletedTask;
        }

        public Task BroadcastEndedAsync(AgentActivityEnded ended, CancellationToken cancellationToken)
        {
            Ended.Add(ended);
            return Task.CompletedTask;
        }
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan by) => _utcNow += by;
    }
}
