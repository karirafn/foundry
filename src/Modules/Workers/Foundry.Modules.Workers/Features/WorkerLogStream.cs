using Foundry.Modules.Workers.Domain;

using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Workers.Features;

internal sealed class WorkerLogStream(DbContext db, IWorkerOrchestrator orchestrator) : IWorkerLogStream
{
    public async IAsyncEnumerable<string> StreamAsync(
        Guid workerRunId,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        WorkerRunId id = WorkerRunId.From(workerRunId);

        WorkerRun? run = await db.Set<WorkerRun>()
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

        if (run is not ActiveRun activeRun)
        {
            yield break;
        }

        // containerId is resolved server-side and must never be exposed to clients.
        string containerId = activeRun.ContainerId.Value;

        await foreach (string line in orchestrator.StreamLogsAsync(containerId, cancellationToken))
        {
            yield return line;
        }
    }
}
