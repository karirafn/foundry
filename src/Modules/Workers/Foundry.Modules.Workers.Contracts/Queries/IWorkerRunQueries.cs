using Foundry.Shared;

namespace Foundry.Modules.Workers.Contracts.Queries;

public interface IWorkerRunQueries
{
    Task<Result<WorkerRunDetail>> GetWorkerRunDetailAsync(
        Guid workerRunId,
        CancellationToken cancellationToken);

    Task<WorkerRunLogResult> GetWorkerRunLogAsync(
        Guid workerRunId,
        CancellationToken cancellationToken);
}

/// <summary>
/// Discriminated result for the worker run log query.
/// </summary>
public abstract class WorkerRunLogResult
{
    private WorkerRunLogResult() { }

    /// <summary>The worker run was not found.</summary>
    public sealed class RunNotFound : WorkerRunLogResult;

    /// <summary>The run exists but has no stored log output (e.g. active or completed without output).</summary>
    public sealed class NoLog : WorkerRunLogResult;

    /// <summary>The run is a failed run with stored container output.</summary>
    public sealed class LogAvailable(string logText) : WorkerRunLogResult
    {
        public string LogText { get; } = logText;
    }
}
