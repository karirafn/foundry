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

    Task<IReadOnlyDictionary<Guid, RunAggregate>> GetRunAggregatesForIssuesAsync(
        IReadOnlyCollection<Guid> issueIds,
        CancellationToken cancellationToken);

    Task<RunTotals> GetRunTotalsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken);

    Task<int> CountConsecutiveTransientRunsAsync(
        Guid issueId,
        int maxAttempts,
        CancellationToken cancellationToken);

    Task<IReadOnlyCollection<WorkerActivity>> GetActiveRunActivityAsync(
        CancellationToken cancellationToken);

    /// <summary>
    /// The publish phase of each issue's run that is publishing or retrying a publish (ADR 0100), keyed by issue ID.
    /// Issues whose run is in neither phase are absent.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, WorkerRunPublishPhase>> GetPublishPhasesForIssuesAsync(
        IReadOnlyCollection<Guid> issueIds,
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

    /// <summary>The run has a stored Run Transcript, already rendered.</summary>
    public sealed class TranscriptAvailable(string transcript) : WorkerRunLogResult
    {
        public string Transcript { get; } = transcript;
    }

    /// <summary>The run is a finished run with stored container output but no Run Transcript.</summary>
    public sealed class LogAvailable(string logText) : WorkerRunLogResult
    {
        public string LogText { get; } = logText;
    }
}
