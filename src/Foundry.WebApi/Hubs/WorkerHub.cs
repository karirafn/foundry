using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Contracts.Queries;
using Foundry.Modules.Workers.Features.Runs;

using Microsoft.AspNetCore.SignalR;

namespace Foundry.WebApi.Hubs;

public sealed class WorkerHub(IWorkerLogStream logStream, IWorkerRunQueries workerRunQueries) : Hub
{
    private const string WorkerActivityMethod = "WorkerActivity";

    public override async Task OnConnectedAsync()
    {
        IReadOnlyCollection<WorkerActivity> activities =
            await workerRunQueries.GetActiveRunActivityAsync(CancellationToken.None);

        foreach (WorkerActivity activity in activities)
        {
            await Clients.Caller.SendAsync(WorkerActivityMethod, activity, CancellationToken.None);
        }

        await base.OnConnectedAsync();
    }

    public IAsyncEnumerable<string> StreamLog(Guid workerRunId, CancellationToken cancellationToken) =>
        logStream.StreamAsync(workerRunId, cancellationToken);
}
