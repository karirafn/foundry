using System.Text.Json.Serialization;

using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Shared;

namespace Foundry.Modules.Settings.Domain.ValueObjects;

public sealed record WorkerMounts
{
    [JsonConstructor]
    private WorkerMounts(IReadOnlyList<WorkerMount> mounts)
    {
        Mounts = mounts;
    }

    public static readonly WorkerMounts Default = new([]);

    public IReadOnlyList<WorkerMount> Mounts { get; }

    public static Result<WorkerMounts> Create(IReadOnlyList<WorkerMount> mounts)
    {
        HashSet<string> seenContainerPaths = new(StringComparer.Ordinal);
        List<WorkerMount> canonicalMounts = [];

        foreach (WorkerMount mount in mounts)
        {
            Result<WorkerMount> validated = Validate(mount);

            if (validated is not Result<WorkerMount>.Success success)
            {
                return Result<WorkerMounts>.Fail(((Result<WorkerMount>.Failure)validated).Error);
            }

            if (!seenContainerPaths.Add(success.Value.ContainerPath))
            {
                return Result<WorkerMounts>.Fail(SettingsErrors.DuplicateMountContainerPath(mount.ContainerPath));
            }

            canonicalMounts.Add(success.Value);
        }

        return Result<WorkerMounts>.Ok(new WorkerMounts(canonicalMounts));
    }

    public bool Equals(WorkerMounts? other) =>
        other is not null && Mounts.SequenceEqual(other.Mounts);

    public override int GetHashCode()
    {
        HashCode hash = new();
        foreach (WorkerMount mount in Mounts)
        {
            hash.Add(mount);
        }

        return hash.ToHashCode();
    }

    private static Result<WorkerMount> Validate(WorkerMount mount)
    {
        if (string.IsNullOrWhiteSpace(mount.ContainerPath) || !mount.ContainerPath.StartsWith('/'))
        {
            return Result<WorkerMount>.Fail(SettingsErrors.MountContainerPathNotAbsolute(mount.ContainerPath));
        }

        if (MountPathRules.ContainsPathTraversal(mount.ContainerPath))
        {
            return Result<WorkerMount>.Fail(SettingsErrors.MountPathTraversal(mount.ContainerPath));
        }

        if (MountPathRules.HasInvalidContainerPathCharacters(mount.ContainerPath))
        {
            return Result<WorkerMount>.Fail(SettingsErrors.InvalidMountPathCharacters(mount.ContainerPath));
        }

        if (MountPathRules.IsSensitiveContainerPath(mount.ContainerPath))
        {
            return Result<WorkerMount>.Fail(SettingsErrors.SensitiveMountContainerPath(mount.ContainerPath));
        }

        if (string.IsNullOrWhiteSpace(mount.HostPath) || !MountPathRules.IsAbsolutePath(mount.HostPath))
        {
            return Result<WorkerMount>.Fail(SettingsErrors.MountHostPathNotAbsolute(mount.HostPath));
        }

        if (MountPathRules.ContainsPathTraversal(mount.HostPath))
        {
            return Result<WorkerMount>.Fail(SettingsErrors.MountPathTraversal(mount.HostPath));
        }

        if (MountPathRules.HasInvalidHostPathCharacters(mount.HostPath))
        {
            return Result<WorkerMount>.Fail(SettingsErrors.InvalidMountPathCharacters(mount.HostPath));
        }

        if (MountPathRules.IsSensitiveHostPath(mount.HostPath))
        {
            return Result<WorkerMount>.Fail(SettingsErrors.SensitiveMountHostPath(mount.HostPath));
        }

        return Result<WorkerMount>.Ok(new WorkerMount(
            MountPathRules.Canonicalize(mount.ContainerPath),
            MountPathRules.CanonicalizeHostPath(mount.HostPath),
            mount.ReadOnly));
    }
}
