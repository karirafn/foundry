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

public sealed class OnConnectedAsync
{
    [Fact]
    public async Task WhenNoActiveRuns_SendsNoMessages()
    {
        // Arrange
        SpyHubCallerClients clients = new();
        NullWorkerRunQueries queries = new();
        StubWorkerLogStream logStream = new([]);
        WorkerHub sut = CreateHub(logStream, queries, clients);

        // Act
        await sut.OnConnectedAsync();

        // Assert
        clients.CallerProxy.SentActivities.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenActiveRunsExist_SendsWorkerActivityForEachActiveRunToCaller()
    {
        // Arrange
        WorkerActivity activity1 = new(
            WorkerRunId: WorkerRunId.New(),
            IssueId: Guid.NewGuid(),
            LastActivityAt: DateTimeOffset.UtcNow);

        WorkerActivity activity2 = new(
            WorkerRunId: WorkerRunId.New(),
            IssueId: Guid.NewGuid(),
            LastActivityAt: DateTimeOffset.UtcNow);

        SpyWorkerRunQueries queries = new([activity1, activity2]);
        SpyHubCallerClients clients = new();
        StubWorkerLogStream logStream = new([]);
        WorkerHub sut = CreateHub(logStream, queries, clients);

        // Act
        await sut.OnConnectedAsync();

        // Assert
        clients.CallerProxy.SentActivities.Count.ShouldBe(2);
        clients.CallerProxy.SentActivities.ShouldContain(activity1);
        clients.CallerProxy.SentActivities.ShouldContain(activity2);
    }

    [Fact]
    public async Task WhenActiveRunsExist_SendsUsingWorkerActivityMethodName()
    {
        // Arrange
        WorkerActivity activity = new(
            WorkerRunId: WorkerRunId.New(),
            IssueId: Guid.NewGuid(),
            LastActivityAt: DateTimeOffset.UtcNow);

        SpyWorkerRunQueries queries = new([activity]);
        SpyHubCallerClients clients = new();
        StubWorkerLogStream logStream = new([]);
        WorkerHub sut = CreateHub(logStream, queries, clients);

        // Act
        await sut.OnConnectedAsync();

        // Assert
        clients.CallerProxy.SentMethods.ShouldAllBe(m => m == "WorkerActivity");
    }

    [Fact]
    public async Task WhenActiveRunsExist_SendsOnlyToCaller_NotAllClients()
    {
        // Arrange
        WorkerActivity activity = new(
            WorkerRunId: WorkerRunId.New(),
            IssueId: Guid.NewGuid(),
            LastActivityAt: DateTimeOffset.UtcNow);

        SpyWorkerRunQueries queries = new([activity]);
        SpyHubCallerClients clients = new();
        StubWorkerLogStream logStream = new([]);
        WorkerHub sut = CreateHub(logStream, queries, clients);

        // Act
        await sut.OnConnectedAsync();

        // Assert
        clients.AllProxy.SentActivities.ShouldBeEmpty();
        clients.CallerProxy.SentActivities.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task WhenAgentActivityProjectionsExist_ReplaysEachToCallerAsAgentActivity()
    {
        // Arrange
        AgentActivityStore store = new();
        AgentActivity first = CreateAgentActivity();
        AgentActivity second = CreateAgentActivity();
        store.Set(first);
        store.Set(second);
        SpyHubCallerClients clients = new();
        WorkerHub sut = CreateHub(new StubWorkerLogStream([]), new NullWorkerRunQueries(), clients, store);

        // Act
        await sut.OnConnectedAsync();

        // Assert
        clients.CallerProxy.SentMethods.ShouldBe(["AgentActivity", "AgentActivity"]);
        clients.CallerProxy.SentArguments.ShouldContain(first);
        clients.CallerProxy.SentArguments.ShouldContain(second);
        clients.AllProxy.SentMethods.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenBothActivityKindsExist_ReplaysWorkerActivityBeforeAgentActivity()
    {
        // Arrange
        WorkerActivity activity = new(
            WorkerRunId: WorkerRunId.New(),
            IssueId: Guid.NewGuid(),
            LastActivityAt: DateTimeOffset.UtcNow);
        AgentActivityStore store = new();
        store.Set(CreateAgentActivity());
        SpyHubCallerClients clients = new();
        WorkerHub sut = CreateHub(new StubWorkerLogStream([]), new SpyWorkerRunQueries([activity]), clients, store);

        // Act
        await sut.OnConnectedAsync();

        // Assert
        clients.CallerProxy.SentMethods.ShouldBe(["WorkerActivity", "AgentActivity"]);
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
        StubWorkerLogStream logStream,
        IWorkerRunQueries queries,
        SpyHubCallerClients clients,
        AgentActivityStore? agentActivityStore = null)
    {
        WorkerHub hub = new(logStream, queries, agentActivityStore ?? new AgentActivityStore());
        hub.Clients = clients;
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

    private sealed class StubWorkerLogStream(IReadOnlyList<string> lines) : IWorkerLogStream
    {
        public async IAsyncEnumerable<string> StreamAsync(
            Guid workerRunId,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (string line in lines)
            {
                yield return line;
                await Task.Yield();
            }
        }
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
        public List<WorkerActivity> SentActivities { get; } = [];
        public List<object?> SentArguments { get; } = [];

        public Task SendCoreAsync(
            string method,
            object?[] args,
            CancellationToken cancellationToken = default)
        {
            SentMethods.Add(method);
            SentArguments.Add(args.Length > 0 ? args[0] : null);

            if (args.Length > 0 && args[0] is WorkerActivity activity)
            {
                SentActivities.Add(activity);
            }

            return Task.CompletedTask;
        }
    }
}
