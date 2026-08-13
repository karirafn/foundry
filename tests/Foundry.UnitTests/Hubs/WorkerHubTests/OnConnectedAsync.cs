using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Contracts.Queries;
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
            WorkerRunId: Guid.NewGuid(),
            IssueId: Guid.NewGuid(),
            LastActivityAt: DateTimeOffset.UtcNow,
            CommitCount: 2);

        WorkerActivity activity2 = new(
            WorkerRunId: Guid.NewGuid(),
            IssueId: Guid.NewGuid(),
            LastActivityAt: DateTimeOffset.UtcNow,
            CommitCount: 5);

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
            WorkerRunId: Guid.NewGuid(),
            IssueId: Guid.NewGuid(),
            LastActivityAt: DateTimeOffset.UtcNow,
            CommitCount: 1);

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
            WorkerRunId: Guid.NewGuid(),
            IssueId: Guid.NewGuid(),
            LastActivityAt: DateTimeOffset.UtcNow,
            CommitCount: 3);

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

    private static WorkerHub CreateHub(
        StubWorkerLogStream logStream,
        IWorkerRunQueries queries,
        SpyHubCallerClients clients)
    {
        WorkerHub hub = new(logStream, queries);
        hub.Clients = clients;
        return hub;
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
            => Task.FromResult(new RunTotals(0, 0L, 0, 0m, 0L, 0L));

        public Task<int> CountConsecutiveTransientRunsAsync(
            Guid issueId,
            int maxAttempts,
            CancellationToken cancellationToken)
            => Task.FromResult(0);
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

        public Task SendCoreAsync(
            string method,
            object?[] args,
            CancellationToken cancellationToken = default)
        {
            SentMethods.Add(method);

            if (args.Length > 0 && args[0] is WorkerActivity activity)
            {
                SentActivities.Add(activity);
            }

            return Task.CompletedTask;
        }
    }
}
