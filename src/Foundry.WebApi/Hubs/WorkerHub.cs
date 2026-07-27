using Foundry.Modules.Workers.Features;
using Foundry.Modules.Workers.Features.Runs;

using Microsoft.AspNetCore.SignalR;

namespace Foundry.WebApi.Hubs;

public sealed class WorkerHub(IWorkerLogStream logStream) : Hub
{
    public IAsyncEnumerable<string> StreamLog(Guid workerRunId, CancellationToken cancellationToken) =>
        logStream.StreamAsync(workerRunId, cancellationToken);
}
