namespace Foundry.Modules.Settings.Contracts;

public sealed record WorkerResourceLimitsSnapshot(int MemoryLimitMb, double CpuLimit, int PidsLimit);
