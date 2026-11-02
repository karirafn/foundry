using System.Collections.Concurrent;

using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Contracts.Queries;

namespace Foundry.Modules.Workers.Features.Activity;

/// <summary>
/// Holds the latest broadcast Agent Activity snapshot per active run, so <c>WorkerHub</c>
/// can replay it to a client that connects mid-run.
/// </summary>
internal sealed class AgentActivityStore : IAgentActivityQueries
{
    private readonly ConcurrentDictionary<WorkerRunId, AgentActivity> _current = new();

    public IReadOnlyCollection<AgentActivity> GetCurrent() => [.. _current.Values];

    public void Set(AgentActivity activity) => _current[activity.WorkerRunId] = activity;

    public void Remove(WorkerRunId workerRunId) => _current.TryRemove(workerRunId, out _);
}
