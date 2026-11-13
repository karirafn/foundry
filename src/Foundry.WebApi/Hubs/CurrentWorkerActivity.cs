using Foundry.Modules.Workers.Contracts;

namespace Foundry.WebApi.Hubs;

/// <summary>
/// The live state of every active run, as <see cref="WorkerHub.GetCurrentActivity"/> returns it: a
/// client replaces what it holds with this after each connect.
/// </summary>
public sealed record CurrentWorkerActivity(
    IReadOnlyCollection<WorkerActivity> WorkerActivities,
    IReadOnlyCollection<AgentActivity> AgentActivities);
