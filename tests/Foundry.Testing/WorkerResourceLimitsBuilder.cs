using Foundry.Modules.Settings.Domain.ValueObjects;
using Foundry.Shared;

namespace Foundry.Testing;

/// <summary>
/// Test data builder for <see cref="WorkerResourceLimits"/>. Defaults to the production defaults;
/// <see cref="Build"/> returns the <see cref="Result{T}"/> so tests can also exercise rejected values.
/// </summary>
public sealed class WorkerResourceLimitsBuilder
{
    private int _memoryLimitMb = 8192;
    private double _cpuLimit = 2.0;
    private int _pidsLimit = 512;

    public WorkerResourceLimitsBuilder WithMemoryLimitMb(int memoryLimitMb)
    {
        _memoryLimitMb = memoryLimitMb;
        return this;
    }

    public WorkerResourceLimitsBuilder WithCpuLimit(double cpuLimit)
    {
        _cpuLimit = cpuLimit;
        return this;
    }

    public WorkerResourceLimitsBuilder WithPidsLimit(int pidsLimit)
    {
        _pidsLimit = pidsLimit;
        return this;
    }

    public Result<WorkerResourceLimits> Build() =>
        WorkerResourceLimits.Create(_memoryLimitMb, _cpuLimit, _pidsLimit);
}
