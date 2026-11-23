using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Contracts.Queries;
using Foundry.Shared;

namespace Foundry.Testing;

/// <summary>
/// In-memory <see cref="IWorkerRunQueries"/> serving the run aggregates and run phases a test sets, and recording which
/// issues were asked for their run phase.
/// </summary>
public sealed class StubWorkerRunQueries : IWorkerRunQueries
{
    private readonly Dictionary<Guid, RunAggregate> _aggregates = [];
    private readonly Dictionary<Guid, WorkerRunPhase> _runPhases = [];

    public void SetAggregate(Guid issueId, RunAggregate aggregate)
    {
        _aggregates[issueId] = aggregate;
    }

    public Task<IReadOnlyDictionary<Guid, RunAggregate>> GetRunAggregatesForIssuesAsync(
        IReadOnlyCollection<Guid> issueIds,
        CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyDictionary<Guid, RunAggregate>>(
            issueIds
                .Where(_aggregates.ContainsKey)
                .ToDictionary(id => id, id => _aggregates[id]));

    public Task<Result<WorkerRunDetail>> GetWorkerRunDetailAsync(
        Guid workerRunId,
        CancellationToken cancellationToken)
        => Task.FromResult(Result<WorkerRunDetail>.Fail(new Error("Test.NotFound", "Not found")));

    public Task<WorkerRunLogResult> GetWorkerRunLogAsync(
        Guid workerRunId,
        CancellationToken cancellationToken)
        => Task.FromResult<WorkerRunLogResult>(new WorkerRunLogResult.RunNotFound());

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

    public Task<IReadOnlyCollection<WorkerActivity>> GetActiveRunActivityAsync(
        CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyCollection<WorkerActivity>>([]);

    public void SetRunPhase(Guid issueId, WorkerRunPhase phase)
    {
        _runPhases[issueId] = phase;
    }

    private readonly List<Guid> _askedRunPhaseIssueIds = [];

    public IReadOnlyList<Guid> AskedRunPhaseIssueIds => _askedRunPhaseIssueIds;

    public Task<IReadOnlyDictionary<Guid, WorkerRunPhase>> GetRunPhasesForIssuesAsync(
        IReadOnlyCollection<Guid> issueIds,
        CancellationToken cancellationToken)
    {
        _askedRunPhaseIssueIds.AddRange(issueIds);
        return Task.FromResult<IReadOnlyDictionary<Guid, WorkerRunPhase>>(
            issueIds
                .Where(_runPhases.ContainsKey)
                .ToDictionary(id => id, id => _runPhases[id]));
    }

    public Task<IReadOnlySet<Guid>> GetIssueIdsWithUnpublishedRunAsync(
        IReadOnlyCollection<Guid> issueIds,
        CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlySet<Guid>>(issueIds.Where(_runPhases.ContainsKey).ToHashSet());

    public Task<IReadOnlySet<Guid>> GetIssueIdsWaitingForPublishRetryAsync(
        IReadOnlyCollection<Guid> issueIds,
        CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlySet<Guid>>(new HashSet<Guid>());
}
