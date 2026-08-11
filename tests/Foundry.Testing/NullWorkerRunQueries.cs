using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Contracts.Queries;
using Foundry.Shared;

namespace Foundry.Testing;

public sealed class NullWorkerRunQueries(int consecutiveTransientRuns = 0) : IWorkerRunQueries
{
    public Task<Result<WorkerRunDetail>> GetWorkerRunDetailAsync(
        Guid workerRunId,
        CancellationToken cancellationToken)
        => Task.FromResult(Result<WorkerRunDetail>.Fail(new Error("Test.NotFound", "Not found")));

    public Task<WorkerRunLogResult> GetWorkerRunLogAsync(
        Guid workerRunId,
        CancellationToken cancellationToken)
        => Task.FromResult<WorkerRunLogResult>(new WorkerRunLogResult.RunNotFound());

    public Task<IReadOnlyDictionary<Guid, RunAggregate>> GetRunAggregatesForIssuesAsync(
        IReadOnlyCollection<Guid> issueIds,
        CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyDictionary<Guid, RunAggregate>>(new Dictionary<Guid, RunAggregate>());

    public Task<RunTotals> GetRunTotalsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
        => Task.FromResult(new RunTotals(0, 0L, 0, 0m, 0L, 0L));

    public Task<int> CountConsecutiveTransientRunsAsync(
        Guid issueId,
        int maxAttempts,
        CancellationToken cancellationToken)
        => Task.FromResult(consecutiveTransientRuns);
}
