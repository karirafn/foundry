using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Contracts.Queries;
using Foundry.Modules.Workers.Features.Activity;
using Foundry.Modules.Workers.Features.Runs;
using Foundry.Testing;
using Foundry.WebApi.Hubs;

using Microsoft.AspNetCore.SignalR;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Hubs.WorkerHubTests;

public sealed class GetCurrentActivity
{
    [Fact]
    public async Task WhenNoActiveRuns_ReturnsEmptyActivity()
    {
        // Arrange
        WorkerHub sut = CreateHub(new NullWorkerRunQueries());

        // Act
        CurrentWorkerActivity result = await sut.GetCurrentActivity();

        // Assert
        result.WorkerActivities.ShouldBeEmpty();
        result.AgentActivities.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenActiveRunsExist_ReturnsTheWorkerActivityOfEachActiveRun()
    {
        // Arrange
        WorkerActivity activity1 = CreateWorkerActivity();
        WorkerActivity activity2 = CreateWorkerActivity();
        WorkerHub sut = CreateHub(new SpyWorkerRunQueries([activity1, activity2]));

        // Act
        CurrentWorkerActivity result = await sut.GetCurrentActivity();

        // Assert
        result.WorkerActivities.ShouldBe([activity1, activity2], ignoreOrder: true);
    }

    [Fact]
    public async Task WhenAgentActivityProjectionsExist_ReturnsEachProjection()
    {
        // Arrange
        AgentActivityStore store = new();
        AgentActivity first = CreateAgentActivity();
        AgentActivity second = CreateAgentActivity();
        store.Set(first);
        store.Set(second);
        WorkerHub sut = CreateHub(new NullWorkerRunQueries(), store);

        // Act
        CurrentWorkerActivity result = await sut.GetCurrentActivity();

        // Assert
        result.AgentActivities.ShouldBe([first, second], ignoreOrder: true);
    }

    [Fact]
    public async Task WhenAClientConnects_PushesNoActivity()
    {
        // Arrange
        AgentActivityStore store = new();
        store.Set(CreateAgentActivity());
        SpyHubCallerClients clients = new();
        WorkerHub sut = CreateHub(new SpyWorkerRunQueries([CreateWorkerActivity()]), store, clients);

        // Act
        await sut.OnConnectedAsync();

        // Assert
        clients.CallerProxy.SentMethods.ShouldBeEmpty();
        clients.AllProxy.SentMethods.ShouldBeEmpty();
    }

    private static WorkerActivity CreateWorkerActivity()
    {
        return new WorkerActivity(
            WorkerRunId: WorkerRunId.New(),
            IssueId: Guid.NewGuid(),
            LastActivityAt: DateTimeOffset.UtcNow);
    }

    private static AgentActivity CreateAgentActivity()
    {
        return new AgentActivity(
            WorkerRunId: WorkerRunId.New(),
            IssueId: Guid.NewGuid(),
            Role: "implement",
            LastEventAt: DateTimeOffset.UtcNow,
            Tasks: [],
            CurrentTask: null,
            CompletedTaskCount: 0,
            Subagents: [],
            ActiveSkill: null,
            InFlightTool: new InFlightTool("Bash", "dotnet test", DateTimeOffset.UtcNow),
            Tokens: new AgentTokenTotals(1, 2));
    }

    private static WorkerHub CreateHub(
        IWorkerRunQueries queries,
        AgentActivityStore? agentActivityStore = null,
        SpyHubCallerClients? clients = null)
    {
        WorkerHub hub = new(new StubWorkerLogStream(), queries, agentActivityStore ?? new AgentActivityStore());
        hub.Clients = clients ?? new SpyHubCallerClients();
        hub.Context = new StubHubCallerContext();
        return hub;
    }

    private sealed class StubHubCallerContext : HubCallerContext
    {
        public override string ConnectionId => "test-connection";
        public override string? UserIdentifier => null;
        public override System.Security.Claims.ClaimsPrincipal? User => null;
        public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();
        public override Microsoft.AspNetCore.Http.Features.IFeatureCollection Features { get; } =
            new Microsoft.AspNetCore.Http.Features.FeatureCollection();
        public override CancellationToken ConnectionAborted => CancellationToken.None;

        public override void Abort() { }
    }

    private sealed class StubWorkerLogStream : IWorkerLogStream
    {
        public IAsyncEnumerable<string> StreamAsync(Guid workerRunId, CancellationToken cancellationToken)
            => AsyncEnumerable.Empty<string>();
    }

    private sealed class SpyWorkerRunQueries(IReadOnlyCollection<WorkerActivity> activities)
        : IWorkerRunQueries
    {
        public Task<IReadOnlyCollection<WorkerActivity>> GetActiveRunActivityAsync(
            CancellationToken cancellationToken)
            => Task.FromResult(activities);

        public Task<Foundry.Shared.Result<WorkerRunDetail>> GetWorkerRunDetailAsync(
            Guid workerRunId,
            CancellationToken cancellationToken)
            => Task.FromResult(Foundry.Shared.Result<WorkerRunDetail>.Fail(
                new Foundry.Shared.Error("Test.NotFound", "Not found")));

        public Task<WorkerRunLogResult> GetWorkerRunLogAsync(
            Guid workerRunId,
            CancellationToken cancellationToken)
            => Task.FromResult<WorkerRunLogResult>(new WorkerRunLogResult.RunNotFound());

        public Task<IReadOnlyDictionary<Guid, RunAggregate>> GetRunAggregatesForIssuesAsync(
            IReadOnlyCollection<Guid> issueIds,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyDictionary<Guid, RunAggregate>>(
                new Dictionary<Guid, RunAggregate>());

        public Task<RunTotals> GetRunTotalsAsync(
            DateTimeOffset from,
            DateTimeOffset to,
            CancellationToken cancellationToken)
            => Task.FromResult(new RunTotals(0, 0L, 0, 0m, TokenTotal.None, TokenTotal.None));

        public Task<int> CountConsecutiveTransientRunsAsync(
            Guid issueId,
            int maxAttempts,
            CancellationToken cancellationToken)
            => Task.FromResult(0);

        public Task<IReadOnlyDictionary<Guid, WorkerRunPublishPhase>> GetPublishPhasesForIssuesAsync(
            IReadOnlyCollection<Guid> issueIds,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyDictionary<Guid, WorkerRunPublishPhase>>(
                new Dictionary<Guid, WorkerRunPublishPhase>());
    }

    private sealed class SpyHubCallerClients : IHubCallerClients
    {
        public SpyClientProxy AllProxy { get; } = new();
        public SpyClientProxy CallerProxy { get; } = new();

        public IClientProxy All => AllProxy;
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => AllProxy;
        public IClientProxy Caller => CallerProxy;
        public IClientProxy Client(string connectionId) => AllProxy;
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => AllProxy;
        public IClientProxy Group(string groupName) => AllProxy;
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => AllProxy;
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => AllProxy;
        public IClientProxy OthersInGroup(string groupName) => AllProxy;
        public IClientProxy Others => AllProxy;
        public IClientProxy User(string userId) => AllProxy;
        public IClientProxy Users(IReadOnlyList<string> userIds) => AllProxy;
    }

    private sealed class SpyClientProxy : IClientProxy
    {
        public List<string> SentMethods { get; } = [];

        public Task SendCoreAsync(
            string method,
            object?[] args,
            CancellationToken cancellationToken = default)
        {
            SentMethods.Add(method);
            return Task.CompletedTask;
        }
    }
}
