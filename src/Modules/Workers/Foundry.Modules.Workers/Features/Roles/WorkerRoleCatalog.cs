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

/// <summary>Everything a role folder contributes to a run: its plugin, prompt templates and verdict schema.</summary>
internal sealed record WorkerRoleDefinition(
    WorkerRole Role,
    RolePlugin Plugin,
    string SystemPromptTemplate,
    string TaskPromptTemplate,
    string? VerdictSchema);

/// <summary>The roles read from one build context, plus the shared plugin loaded for every role.</summary>
internal sealed record WorkerRoleCatalogContent(IReadOnlyList<WorkerRoleDefinition> Roles, RolePlugin Shared)
{
    public WorkerRoleDefinition? Find(WorkerRole role) =>
        Roles.FirstOrDefault(definition => definition.Role == role);
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
