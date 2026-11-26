using Foundry.Modules.Workers.Contracts;

namespace Foundry.Modules.Workers.Domain.ValueObjects;

/// <summary>
/// What a run was dispatched with (ADR 0095): an implement run's effective Implement Demand (<see langword="null"/>
/// for triage and review), and the model and effort written to its managed settings, each empty when none was written.
/// </summary>
public sealed record RunModel
{
    public RunModel(ImplementDemand? demand, string model, string effort)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(effort);

        Demand = demand;
        Model = model;
        Effort = effort;
    }

    public ImplementDemand? Demand { get; }

    public string Model { get; }

    public string Effort { get; }
}
