using Foundry.Shared;

namespace Foundry.Modules.Workers.Contracts.Queries;

public interface IWorkerRunQueries
{
    Task<Result<WorkerRunDetail>> GetWorkerRunDetailAsync(
        Guid workerRunId,
        CancellationToken cancellationToken);
}
