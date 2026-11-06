namespace Foundry.Modules.Workers.Domain.ValueObjects;

/// <summary>
/// The job a worker run is dispatched to do (ADR 0095). Each role brings its own Claude Code plugin and prompts
/// from <c>workers/roles/&lt;role&gt;/</c>, plus the <see cref="SharedFolder"/> plugin loaded for every role. Role names
/// come from code: a role listed in <see cref="All"/> without a folder fails the unit tests and the startup image build.
/// </summary>
public sealed record WorkerRole
{
    /// <summary>The folder of the plugin the entrypoint loads for every role. It is never a role itself.</summary>
    public const string SharedFolder = "shared";

    public static readonly WorkerRole Implement = new("implement");

    /// <summary>Every role Foundry can dispatch, in display order.</summary>
    public static readonly IReadOnlyList<WorkerRole> All = [Implement];

    private WorkerRole(string name)
    {
        Name = name;
    }

    public string Name { get; }

    /// <summary>
    /// Rehydrates a role recorded on a worker run. Unknown names are kept as recorded so history from a role
    /// that was later removed still loads.
    /// </summary>
    public static WorkerRole FromStored(string name) =>
        All.FirstOrDefault(role => string.Equals(role.Name, name, StringComparison.Ordinal)) ?? new WorkerRole(name);

    public override string ToString() => Name;
}
