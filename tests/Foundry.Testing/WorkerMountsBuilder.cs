using Foundry.Modules.Settings.Domain.ValueObjects;
using Foundry.Shared;

namespace Foundry.Testing;

/// <summary>
/// Test data builder for <see cref="WorkerMounts"/>. Starts empty; <see cref="Build"/> returns the
/// <see cref="Result{T}"/> so tests can also exercise rejected mount lists.
/// </summary>
public sealed class WorkerMountsBuilder
{
    private readonly List<WorkerMount> _mounts = [];

    public WorkerMountsBuilder WithMount(WorkerMount mount)
    {
        _mounts.Add(mount);
        return this;
    }

    public WorkerMountsBuilder WithMount(string containerPath, string hostPath, bool readOnly = true) =>
        WithMount(new WorkerMountBuilder()
            .WithContainerPath(containerPath)
            .WithHostPath(hostPath)
            .WithReadOnly(readOnly)
            .Build());

    public Result<WorkerMounts> Build() => WorkerMounts.Create(_mounts);
}

public sealed class WorkerMountBuilder
{
    private string _containerPath = "/skills";
    private string _hostPath = "/home/user/skills";
    private bool _readOnly = true;

    public WorkerMountBuilder WithContainerPath(string containerPath)
    {
        _containerPath = containerPath;
        return this;
    }

    public WorkerMountBuilder WithHostPath(string hostPath)
    {
        _hostPath = hostPath;
        return this;
    }

    public WorkerMountBuilder WithReadOnly(bool readOnly)
    {
        _readOnly = readOnly;
        return this;
    }

    public WorkerMount Build() => new(_containerPath, _hostPath, _readOnly);
}
