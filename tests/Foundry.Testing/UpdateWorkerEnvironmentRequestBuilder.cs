namespace Foundry.Testing;

/// <summary>
/// Test data builder for the <c>PUT /api/settings/worker-environment</c> request body.
/// Defaults to a valid body with no mounts.
/// </summary>
public sealed class UpdateWorkerEnvironmentRequestBuilder
{
    private readonly List<object> _mounts = [];
    private int _memoryLimitMb = 4096;
    private double _cpuLimit = 4.0;
    private int _pidsLimit = 1024;

    public UpdateWorkerEnvironmentRequestBuilder WithMemoryLimitMb(int memoryLimitMb)
    {
        _memoryLimitMb = memoryLimitMb;
        return this;
    }

    public UpdateWorkerEnvironmentRequestBuilder WithCpuLimit(double cpuLimit)
    {
        _cpuLimit = cpuLimit;
        return this;
    }

    public UpdateWorkerEnvironmentRequestBuilder WithPidsLimit(int pidsLimit)
    {
        _pidsLimit = pidsLimit;
        return this;
    }

    public UpdateWorkerEnvironmentRequestBuilder WithMount(string containerPath, string hostPath, bool readOnly = true)
    {
        _mounts.Add(new { containerPath, hostPath, readOnly });
        return this;
    }

    public object Build() => new
    {
        memoryLimitMb = _memoryLimitMb,
        cpuLimit = _cpuLimit,
        pidsLimit = _pidsLimit,
        mounts = _mounts.ToArray(),
    };
}
