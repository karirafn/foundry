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

    /// <summary>
    /// The current activity of every active run (ADR 0063). A client invokes it after each connect and
    /// replaces its state with the result, so a run that ended while it was disconnected is dropped
    /// at once, and the client knows when it is up to date.
    /// </summary>
    public async Task<CurrentWorkerActivity> GetCurrentActivity()
    {
        IReadOnlyCollection<WorkerActivity> workerActivities =
            await workerRunQueries.GetActiveRunActivityAsync(Context.ConnectionAborted);

        return new CurrentWorkerActivity(workerActivities, agentActivityQueries.GetCurrent());
    }

    public IAsyncEnumerable<string> StreamLog(Guid workerRunId, CancellationToken cancellationToken) =>
        logStream.StreamAsync(workerRunId, cancellationToken);
}
