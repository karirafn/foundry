using System.Globalization;

using Foundry.Modules.Workers.Domain.ValueObjects;

namespace Foundry.Modules.Workers.Features.Roles;

/// <summary>A skill or agent file of a role plugin, by its frontmatter name and description.</summary>
internal sealed record RolePluginComponent(string Name, string? Description, string Path, string Content);

/// <summary>A Claude Code plugin under <c>workers/roles/&lt;folder&gt;/</c>.</summary>
internal sealed record RolePlugin(
    string Folder,
    string? Description,
    IReadOnlyList<RolePluginComponent> Skills,
    IReadOnlyList<RolePluginComponent> Agents);

/// <summary>
/// Everything a role contributes to a run: its plugin, prompt templates and verdict schema from its folder, and its
/// <see cref="WorkerCapability"/> from code.
/// </summary>
internal sealed record WorkerRoleDefinition(
    WorkerRole Role,
    WorkerCapability Capability,
    RolePlugin Plugin,
    string SystemPromptTemplate,
    string TaskPromptTemplate,
    string? VerdictSchema);

/// <summary>
/// The roles read from one build context, plus the shared plugin loaded for every role and the Worker Principles each
/// role selects its sections from.
/// </summary>
internal sealed record WorkerRoleCatalogContent(
    IReadOnlyList<WorkerRoleDefinition> Roles,
    RolePlugin Shared,
    WorkerPrinciples Principles)
{
    public WorkerRoleDefinition? Find(WorkerRole role) =>
        Roles.FirstOrDefault(definition => definition.Role == role);

    /// <summary>The Worker Principles <paramref name="role"/>'s system prompt carries.</summary>
    public string PrinciplesFor(WorkerRole role) => Principles.AssembleFor(role.PrincipleSections);
}

/// <summary>
/// The Worker Principles read from <c>shared/prompts/</c>: the framing line and one text per
/// <see cref="PrincipleSection"/>. Every section is required, so any role's selection can be assembled.
/// </summary>
internal sealed class WorkerPrinciples
{
    private const string Separator = "\n\n";
    private const string BulletPrefix = "- ";

    private readonly IReadOnlyDictionary<PrincipleSection, string> _sections;

    public WorkerPrinciples(string framing, IReadOnlyDictionary<PrincipleSection, string> sections)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(framing);
        ArgumentNullException.ThrowIfNull(sections);

        foreach (PrincipleSection section in PrincipleSectionNames.All)
        {
            if (!sections.TryGetValue(section, out string? text) || string.IsNullOrWhiteSpace(text))
            {
                throw new ArgumentException($"The '{section.ToName()}' principle section has no text.", nameof(sections));
            }
        }

        Framing = framing;
        _sections = sections;
    }

    public string Framing { get; }

    /// <summary>The section's file text as read, without principle ids.</summary>
    public string TextOf(PrincipleSection section) => _sections[section];

    /// <summary>
    /// The framing line followed by the selected sections in the fixed order, each bullet prefixed with its principle id
    /// (<c>- [design.1] …</c>). An empty selection yields an empty string: no framing introducing principles that never
    /// follow.
    /// </summary>
    public string AssembleFor(PrincipleSectionSet selection)
    {
        ArgumentNullException.ThrowIfNull(selection);

        if (selection.IsEmpty)
        {
            return string.Empty;
        }

        return string.Join(Separator, [Framing, .. selection.InOrder.Select(NumberedTextOf)]);
    }

    /// <summary>
    /// The principle ids the selected sections carry, in the fixed order: the only ids a verdict assembled from this
    /// selection can cite.
    /// </summary>
    public IReadOnlyList<string> IdsFor(PrincipleSectionSet selection)
    {
        ArgumentNullException.ThrowIfNull(selection);

        return [.. selection.InOrder.SelectMany(section =>
            Enumerable.Range(1, BulletLines(section).Count()).Select(position => PrincipleId(section, position)))];
    }

    /// <summary>The largest text any role can receive: the framing followed by every section.</summary>
    public string AssembleAll() => AssembleFor(PrincipleSectionSet.Of([.. PrincipleSectionNames.All]));

    // Ids are computed from each bullet's position rather than written in the files, so a hand-numbered id can never
    // drift from the line it names.
    private string NumberedTextOf(PrincipleSection section)
    {
        int position = 0;

        return string.Join('\n', TextOf(section).Split('\n').Select(line =>
            IsBullet(line)
                ? $"{BulletPrefix}[{PrincipleId(section, ++position)}] {line[BulletPrefix.Length..]}"
                : line));
    }

    private IEnumerable<string> BulletLines(PrincipleSection section) =>
        TextOf(section).Split('\n').Where(IsBullet);

    private static bool IsBullet(string line) => line.StartsWith(BulletPrefix, StringComparison.Ordinal);

    private static string PrincipleId(PrincipleSection section, int position) =>
        $"{section.ToIdPrefix()}.{position.ToString(CultureInfo.InvariantCulture)}";
}

/// <summary>
/// Which image the catalog belongs to. <see cref="Verified"/> is <see langword="true"/> when the catalog was read from
/// the same build-context tar the image was built from, and <see langword="false"/> when image build is disabled and
/// the catalog was read from disk without proof that the operator-supplied image carries the same roles.
/// </summary>
internal sealed record WorkerImageProvenance(
    string Tag,
    string? ImageId,
    DateTimeOffset LoadedAt,
    string? Commit,
    bool Verified)
{
    /// <summary>The image a run records: the tag, pinned to the image ID when it is known.</summary>
    public string Reference => ImageId is null ? Tag : $"{Tag}@{ImageId}";
}

/// <summary>The role catalog the running workers use, held in memory for dispatch and the role cards.</summary>
internal sealed record WorkerRoleCatalog(WorkerRoleCatalogContent Content, WorkerImageProvenance Image);
