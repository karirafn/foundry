using Foundry.Modules.Workers.Features.ContainerSpec;
using Foundry.Modules.Workers.Features.Publisher;

namespace Foundry.Modules.Workers.Features.ImageBuild;

/// <summary>
/// The short-lived container that runs a worker image's toolchain script: the image pinned by ID, the entrypoint
/// replaced by the script, started as <c>node</c> under the worker hardening, with no environment of Foundry's.
/// </summary>
internal static class ToolchainProbeContainerSpec
{
    public const string ScriptPath = "/opt/foundry/toolchain.sh";

    public const string User = "node";

    // Sized for the slowest version command (csharp-ls starts the .NET runtime), not for a run.
    private const int MemoryLimitMb = 1024;
    private const double CpuLimit = 1.0;
    private const int PidsLimit = 256;

    public static WorkerContainerSpec For(string imageId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imageId);

        Dictionary<string, string> labels = new()
        {
            // Lets the transient-container reaper remove a probe a Foundry crash left behind once it exits.
            [PublisherContainerSpec.TransientLabelKey] = "true",
        };

        return WorkerHardeningSpec.ApplyToToolchainProbe(new(
            imageId,
            new Dictionary<string, string>(),
            labels,
            [])
        {
            Entrypoint = [ScriptPath],
            User = User,
            MemoryLimitMb = MemoryLimitMb,
            CpuLimit = CpuLimit,
            PidsLimit = PidsLimit,
        });
    }
}
