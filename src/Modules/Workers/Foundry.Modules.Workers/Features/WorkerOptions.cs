using Foundry.Modules.Workers.Features.ContainerSpec;

namespace Foundry.Modules.Workers.Features;

internal sealed class WorkerOptions
{
    public IReadOnlyDictionary<string, string> Mounts { get; set; } = new Dictionary<string, string>();

    public IReadOnlyDictionary<string, string> WritableMounts { get; set; } = new Dictionary<string, string>();

    public WorkerSettingsOptions Settings { get; set; } = new();

    /// <summary>Memory limit for worker containers in megabytes.</summary>
    public int MemoryLimitMb { get; set; } = 8192;

    /// <summary>CPU limit for worker containers (number of CPUs, fractional values allowed).</summary>
    public double CpuLimit { get; set; } = 2.0;

    /// <summary>Maximum number of processes (PIDs) per worker container.</summary>
    public int PidsLimit { get; set; } = 512;
}
