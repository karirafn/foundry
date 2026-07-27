namespace Foundry.Modules.Workers.Features.Runs;

// Must be public: WorkerHub (in Foundry.WebApi) injects this interface.
// CA1711 suppressed: the 'Stream' suffix describes the feature (async log streaming), not a Stream subclass.
#pragma warning disable CA1711
public interface IWorkerLogStream
#pragma warning restore CA1711
{
    IAsyncEnumerable<string> StreamAsync(Guid workerRunId, CancellationToken cancellationToken);
}
