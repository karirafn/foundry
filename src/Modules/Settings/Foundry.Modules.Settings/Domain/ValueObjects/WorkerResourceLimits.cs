using System.Text.Json.Serialization;

using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Shared;

namespace Foundry.Modules.Settings.Domain.ValueObjects;

public sealed record WorkerResourceLimits
{
    internal const int MinMemoryLimitMb = 512;
    internal const int MaxMemoryLimitMb = 65536;
    internal const double MinCpuLimit = 0.5;
    internal const double MaxCpuLimit = 32;
    internal const int MinPidsLimit = 64;
    internal const int MaxPidsLimit = 8192;

    private const int DefaultMemoryLimitMb = 8192;
    private const double DefaultCpuLimit = 2.0;
    private const int DefaultPidsLimit = 512;

    [JsonConstructor]
    private WorkerResourceLimits(int memoryLimitMb, double cpuLimit, int pidsLimit)
    {
        MemoryLimitMb = memoryLimitMb;
        CpuLimit = cpuLimit;
        PidsLimit = pidsLimit;
    }

    public static readonly WorkerResourceLimits Default = Create(
        memoryLimitMb: DefaultMemoryLimitMb,
        cpuLimit: DefaultCpuLimit,
        pidsLimit: DefaultPidsLimit)
        .Match(
            onSuccess: v => v,
            onFailure: e => throw new InvalidOperationException($"WorkerResourceLimits.Default is invalid: {e.Message}"));

    public int MemoryLimitMb { get; }

    public double CpuLimit { get; }

    public int PidsLimit { get; }

    public static Result<WorkerResourceLimits> Create(int memoryLimitMb, double cpuLimit, int pidsLimit)
    {
        if (memoryLimitMb is < MinMemoryLimitMb or > MaxMemoryLimitMb)
        {
            return Result<WorkerResourceLimits>.Fail(SettingsErrors.InvalidMemoryLimit(memoryLimitMb));
        }

        if (double.IsNaN(cpuLimit) || cpuLimit is < MinCpuLimit or > MaxCpuLimit)
        {
            return Result<WorkerResourceLimits>.Fail(SettingsErrors.InvalidCpuLimit(cpuLimit));
        }

        if (pidsLimit is < MinPidsLimit or > MaxPidsLimit)
        {
            return Result<WorkerResourceLimits>.Fail(SettingsErrors.InvalidPidsLimit(pidsLimit));
        }

        return Result<WorkerResourceLimits>.Ok(new WorkerResourceLimits(memoryLimitMb, cpuLimit, pidsLimit));
    }
}
