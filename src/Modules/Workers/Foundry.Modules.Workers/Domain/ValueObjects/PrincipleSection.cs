namespace Foundry.Modules.Workers.Domain.ValueObjects;

/// <summary>
/// A named group of Worker Principles, held in one file under <c>workers/roles/shared/prompts/principles/</c>. The
/// declaration order is the fixed order sections follow the shared framing line in a role's system prompt.
/// </summary>
public enum PrincipleSection
{
    Testing,
    Design,
    Code,
    DomainModelling,
}

public static class PrincipleSectionNames
{
    /// <summary>Every section, in the fixed order.</summary>
    public static readonly IReadOnlyList<PrincipleSection> All = Enum.GetValues<PrincipleSection>();

    /// <summary>The section's display name, as shown on the role cards.</summary>
    public static string ToName(this PrincipleSection section) =>
        section switch
        {
            PrincipleSection.Testing => "Testing",
            PrincipleSection.Design => "Design",
            PrincipleSection.Code => "Code",
            PrincipleSection.DomainModelling => "Domain modelling",
            _ => throw new ArgumentOutOfRangeException(nameof(section), section, "Unknown principle section."),
        };

    /// <summary>The section's file name under <c>shared/prompts/principles/</c>.</summary>
    public static string ToFileName(this PrincipleSection section) =>
        section switch
        {
            PrincipleSection.Testing => "testing.md",
            PrincipleSection.Design => "design.md",
            PrincipleSection.Code => "code.md",
            PrincipleSection.DomainModelling => "domain.md",
            _ => throw new ArgumentOutOfRangeException(nameof(section), section, "Unknown principle section."),
        };
}

/// <summary>
/// The Principle Sections a Worker Role's system prompt carries. A section appears at most once, and the sections are
/// always enumerated in the section type's fixed order, whatever order they were declared in.
/// </summary>
public sealed class PrincipleSectionSet : IEquatable<PrincipleSectionSet>
{
    /// <summary>No sections: the role's prompt carries neither the framing nor any principles.</summary>
    public static readonly PrincipleSectionSet None = new([]);

    private PrincipleSectionSet(IReadOnlyList<PrincipleSection> inOrder)
    {
        InOrder = inOrder;
    }

    /// <summary>The selected sections in the fixed order.</summary>
    public IReadOnlyList<PrincipleSection> InOrder { get; }

    public bool IsEmpty => InOrder.Count == 0;

    /// <summary>Selects <paramref name="sections"/>; a section listed twice or one the type does not define is rejected.</summary>
    public static PrincipleSectionSet Of(params PrincipleSection[] sections)
    {
        ArgumentNullException.ThrowIfNull(sections);

        foreach (PrincipleSection section in sections)
        {
            if (!Enum.IsDefined(section))
            {
                throw new ArgumentOutOfRangeException(nameof(sections), section, "Unknown principle section.");
            }
        }

        PrincipleSection[] distinct = [.. sections.Distinct()];

        if (distinct.Length != sections.Length)
        {
            throw new ArgumentException("A principle section can be selected only once.", nameof(sections));
        }

        return new PrincipleSectionSet([.. PrincipleSectionNames.All.Where(distinct.Contains)]);
    }

    public bool Equals(PrincipleSectionSet? other) =>
        other is not null && InOrder.SequenceEqual(other.InOrder);

    public override bool Equals(object? obj) => Equals(obj as PrincipleSectionSet);

    public override int GetHashCode() =>
        InOrder.Aggregate(0, (hash, section) => hash | (1 << (int)section));

    public override string ToString() => string.Join(", ", InOrder.Select(section => section.ToName()));
}
