namespace Foundry.Modules.Workers.Domain.ValueObjects;

/// <summary>
/// How a reviewed commit changed a path, against its merge base with the default branch. Renames are not detected, so a
/// moved file is one <see cref="Deleted"/> and one <see cref="Added"/> path.
/// </summary>
public enum ChangeStatus
{
    Added,
    Modified,
    Deleted,
}
