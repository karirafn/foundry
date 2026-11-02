using System.Text.Json.Serialization;

using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Shared;

namespace Foundry.Modules.Settings.Domain.ValueObjects;

public sealed record WorkerMounts
{
    private const char TrailingSeparator = '/';

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

        foreach (WorkerMount mount in mounts)
        {
            if (Validate(mount) is Error error)
            {
                return Result<WorkerMounts>.Fail(error);
            }

            if (!seenContainerPaths.Add(mount.ContainerPath.TrimEnd(TrailingSeparator)))
            {
                return Result<WorkerMounts>.Fail(SettingsErrors.DuplicateMountContainerPath(mount.ContainerPath));
            }
        }

        return Result<WorkerMounts>.Ok(new WorkerMounts([.. mounts]));
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

    private static Error? Validate(WorkerMount mount)
    {
        if (!mount.ContainerPath.StartsWith('/'))
        {
            return SettingsErrors.MountContainerPathNotAbsolute(mount.ContainerPath);
        }

        if (MountPathRules.ContainsPathTraversal(mount.ContainerPath))
        {
            return SettingsErrors.MountPathTraversal(mount.ContainerPath);
        }

        if (MountPathRules.IsSensitiveContainerPath(mount.ContainerPath))
        {
            return SettingsErrors.SensitiveMountContainerPath(mount.ContainerPath);
        }

        if (!MountPathRules.IsAbsolutePath(mount.HostPath))
        {
            return SettingsErrors.MountHostPathNotAbsolute(mount.HostPath);
        }

        if (MountPathRules.ContainsPathTraversal(mount.HostPath))
        {
            return SettingsErrors.MountPathTraversal(mount.HostPath);
        }

        return MountPathRules.IsSensitiveHostPath(mount.HostPath)
            ? SettingsErrors.SensitiveMountHostPath(mount.HostPath)
            : null;
    }
}
