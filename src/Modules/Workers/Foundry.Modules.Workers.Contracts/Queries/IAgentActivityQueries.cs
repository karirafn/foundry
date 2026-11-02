namespace Foundry.Modules.Workers.Contracts.Queries;

/// <summary>
/// Reads the current in-memory Agent Activity projections, one per active run.
/// </summary>
public interface IAgentActivityQueries
{
    IReadOnlyCollection<AgentActivity> GetCurrent();
}
