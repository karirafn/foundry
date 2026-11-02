using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Contracts.Queries;
using Foundry.Modules.Workers.Features.Runs;

using Microsoft.AspNetCore.SignalR;

namespace Foundry.WebApi.Hubs;

public sealed class WorkerHub(
    IWorkerLogStream logStream,
    IWorkerRunQueries workerRunQueries,
    IAgentActivityQueries agentActivityQueries) : Hub
{
    internal const string AgentActivityMethod = "AgentActivity";
    internal const string AgentActivityEndedMethod = "AgentActivityEnded";

    private const string WorkerActivityMethod = "WorkerActivity";

    public override async Task OnConnectedAsync()
    {
        IReadOnlyCollection<WorkerActivity> activities =
            await workerRunQueries.GetActiveRunActivityAsync(Context.ConnectionAborted);

        foreach (WorkerActivity activity in activities)
        {
            await Clients.Caller.SendAsync(WorkerActivityMethod, activity, Context.ConnectionAborted);
        }

        // Replay the in-memory Agent Activity projection for every active run (ADR 0063).
        foreach (AgentActivity agentActivity in agentActivityQueries.GetCurrent())
        {
            await Clients.Caller.SendAsync(AgentActivityMethod, agentActivity, Context.ConnectionAborted);
        }

        await base.OnConnectedAsync();
    }

    public IAsyncEnumerable<string> StreamLog(Guid workerRunId, CancellationToken cancellationToken) =>
        logStream.StreamAsync(workerRunId, cancellationToken);
}
