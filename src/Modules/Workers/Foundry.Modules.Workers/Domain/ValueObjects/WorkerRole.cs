namespace Foundry.Modules.Workers.Domain.ValueObjects;

/// <summary>
/// The job a worker run is dispatched to do (ADR 0095). Each role brings its own Claude Code plugin and prompts
/// from <c>workers/roles/&lt;role&gt;/</c>, plus the <see cref="SharedFolder"/> plugin loaded for every role. Role names
/// come from code: a role listed in <see cref="All"/> without a folder fails the unit tests and the startup image build.
/// Each role declares the <see cref="WorkerCapability"/> it runs under and the <see cref="PrincipleSection"/>s its
/// system prompt carries in code, never in its folder (ADR 0098).
/// </summary>
public sealed record WorkerRole
{
    /// <summary>The folder of the plugin the entrypoint loads for every role. It is never a role itself.</summary>
    public const string SharedFolder = "shared";

    /// <summary>Judges whether an issue is ready to implement and classifies it. Returns a verdict only.</summary>
    public static readonly WorkerRole Triage = new(
        "triage",
        WorkerCapability.Restricted,
        // Triage judges an issue before any code exists; Testing and Code lines can only be judged in a diff.
        PrincipleSectionSet.Of(PrincipleSection.Design, PrincipleSection.DomainModelling));

    public static readonly WorkerRole Implement = new(
        "implement",
        WorkerCapability.Full,
        PrincipleSectionSet.Of(
            PrincipleSection.Testing,
            PrincipleSection.Design,
            PrincipleSection.Code,
            PrincipleSection.DomainModelling));

    /// <summary>Every role Foundry can dispatch, in display order.</summary>
    public static readonly IReadOnlyList<WorkerRole> All = [Triage, Implement];

    private WorkerRole(string name, WorkerCapability capability, PrincipleSectionSet principleSections)
    {
        Name = name;
        Capability = capability;
        PrincipleSections = principleSections;
    }

    public string Name { get; }

    /// <summary>The capability profile runs of this role get.</summary>
    public WorkerCapability Capability { get; }

    /// <summary>The Principle Sections this role's system prompt carries.</summary>
    public PrincipleSectionSet PrincipleSections { get; }

    /// <summary>
    /// Rehydrates a role recorded on a worker run. Unknown names are kept as recorded so history from a role
    /// that was later removed still loads; such a role is never dispatched, is <see cref="WorkerCapability.Restricted"/>
    /// so it can never be mistaken for one with full access, and carries no principle sections.
    /// </summary>
    public static WorkerRole FromStored(string name) =>
        All.FirstOrDefault(role => string.Equals(role.Name, name, StringComparison.Ordinal))
            ?? new WorkerRole(name, WorkerCapability.Restricted, PrincipleSectionSet.None);

    public override string ToString() => Name;
}
