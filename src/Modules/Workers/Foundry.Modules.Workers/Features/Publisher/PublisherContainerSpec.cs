using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Features.ContainerSpec;

namespace Foundry.Modules.Workers.Features.Publisher;

/// <summary>The Publisher's container (ADR 0100): the Publisher image under the worker hardening with no capability back.</summary>
internal static class PublisherContainerSpec
{
    public const string RunIdLabelKey = "foundry.publisher-run-id";

    /// <summary>
    /// Lets the transient-container reaper remove a Publisher container a Foundry crash left behind once it exits;
    /// a Publisher container is never needed after Foundry has read its exit.
    /// </summary>
    public const string TransientLabelKey = "foundry.transient";

    public static WorkerContainerSpec ForPrepare(PrepareRequest request)
    {
        Dictionary<string, string> environment = new()
        {
            ["CLONE_URL"] = request.CloneUrl.ToString(),
            ["GIT_PAT"] = request.GitPat,
        };

        if (request.RunBranch is { } runBranch)
        {
            environment["RUN_BRANCH"] = runBranch.Value;
        }

        Dictionary<string, string> labels = new()
        {
            [RunIdLabelKey] = request.RunId.Value.ToString(),
            [TransientLabelKey] = "true",
        };

        return WorkerHardeningSpec.ApplyToPublisher(new(
            WorkerImageNames.PublisherImageName,
            environment,
            labels,
            ["prepare"])
        {
            MemoryLimitMb = request.Limits.MemoryLimitMb,
            CpuLimit = request.Limits.CpuLimit,
            PidsLimit = request.Limits.PidsLimit,
            VolumeMounts = [new VolumeMount(WorkerVolumeNames.HandOffVolumeName(request.RunId), WorkerVolumeNames.HandOffContainerPath)],
        });
    }
}
