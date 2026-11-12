using Foundry.Modules.Settings.Contracts;
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

        return Build(request.RunId, environment, "prepare", request.Limits);
    }

    public static WorkerContainerSpec ForPublish(PublishRequest request)
    {
        Dictionary<string, string> environment = new()
        {
            ["CLONE_URL"] = request.CloneUrl.ToString(),
            ["GIT_PAT"] = request.GitPat,
            ["RUN_BRANCH"] = request.RunBranch.Value,
            ["RUN_ID"] = request.RunId.Value.ToString(),
            ["BASE_DEFAULT_SHA"] = request.Base.DefaultBranchHead.Value,
            ["AUTHOR_NAME"] = request.Identity.Name,
            ["AUTHOR_EMAIL"] = request.Identity.Email,
        };

        if (request.Base.RunBranchHead is { } runBranchHead)
        {
            environment["BASE_BRANCH_SHA"] = runBranchHead.Value;
        }

        return Build(request.RunId, environment, "publish", request.Limits);
    }

    private static WorkerContainerSpec Build(
        WorkerRunId runId,
        Dictionary<string, string> environment,
        string mode,
        WorkerResourceLimitsSnapshot limits)
    {
        Dictionary<string, string> labels = new()
        {
            [RunIdLabelKey] = runId.Value.ToString(),
            [TransientLabelKey] = "true",
        };

        return WorkerHardeningSpec.ApplyToPublisher(new(
            WorkerImageNames.PublisherImageName,
            environment,
            labels,
            [mode])
        {
            MemoryLimitMb = limits.MemoryLimitMb,
            CpuLimit = limits.CpuLimit,
            PidsLimit = limits.PidsLimit,
            VolumeMounts = [new VolumeMount(WorkerVolumeNames.HandOffVolumeName(runId), WorkerVolumeNames.HandOffContainerPath)],
        });
    }
}
