using Docker.DotNet;

using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.ContainerSpec;
using Foundry.Modules.Workers.Features.ImageBuild;
using Foundry.Shared.Infrastructure.Docker;

using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Workers.Infrastructure;

/// <summary>Runs a worker image's toolchain script in a throwaway container, the way the Publisher probe runs.</summary>
internal sealed class DockerWorkerImageToolchainProbe(
    IDockerContainerRuntime runtime,
    ILogger<DockerWorkerImageToolchainProbe> logger) : IWorkerImageToolchainProbe
{
    /// <summary>Bounds a probe: the script's eleven version commands take seconds, each capped at 20.</summary>
    internal static readonly TimeSpan ProbeTimeout = TimeSpan.FromMinutes(2);

    public async Task<Toolchain?> ProbeAsync(string imageId, CancellationToken cancellationToken)
    {
        WorkerContainerSpec spec = ToolchainProbeContainerSpec.For(imageId);
        string? containerId = null;

        try
        {
            containerId = await runtime.CreateAndStartAsync(DockerWorkerOrchestrator.ToCreateParameters(spec), cancellationToken);

            long? exitCode = await WaitWithinTimeoutAsync(containerId, cancellationToken);

            if (exitCode is null)
            {
                logger.LogWarning(
                    "The toolchain probe of worker image {ImageId} did not finish within {Minutes} minutes.",
                    imageId,
                    ProbeTimeout.TotalMinutes);
                return null;
            }

            string? log = await runtime.GetAllLogsAsync(containerId, cancellationToken);

            if (exitCode.Value != 0)
            {
                logger.LogWarning(
                    "The toolchain probe of worker image {ImageId} exited with code {ExitCode}.",
                    imageId,
                    exitCode.Value);
                return null;
            }

            Toolchain? toolchain = ToolchainLineParser.Parse(log);

            if (toolchain is null)
            {
                logger.LogWarning("The toolchain probe of worker image {ImageId} reported no valid toolchain line.", imageId);
            }

            return toolchain;
        }
        catch (DockerApiException ex)
        {
            logger.LogWarning(
                "The toolchain probe of worker image {ImageId} could not run: {Error}",
                imageId,
                DockerWorkerOrchestrator.RedactDockerError(ex.Message));
            return null;
        }
        finally
        {
            if (containerId is not null)
            {
                await TryRemoveAsync(containerId);
            }
        }
    }

    private async Task<long?> WaitWithinTimeoutAsync(string containerId, CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ProbeTimeout);

        try
        {
            return await runtime.WaitAsync(containerId, timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    // Removal also stops a probe that timed out, and deletes its anonymous scratch volumes. It runs even when the
    // probe was cancelled, so no probe container outlives it.
    private async Task TryRemoveAsync(string containerId)
    {
        try
        {
            await runtime.RemoveAsync(containerId, CancellationToken.None);
        }
#pragma warning disable CA1031 // Removal is best-effort; the transient-container reaper removes an exited leftover.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            logger.LogWarning(
                "Could not remove toolchain probe container {ContainerId}: {Error}",
                containerId,
                SecretRedactor.Redact(ex.Message));
        }
    }
}
