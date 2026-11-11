using Docker.DotNet;
using Docker.DotNet.Models;

using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.ContainerSpec;
using Foundry.Modules.Workers.Features.Publisher;
using Foundry.Shared.Infrastructure.Docker;

using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Workers.Infrastructure;

internal sealed class DockerPublisher(IDockerContainerRuntime runtime, ILogger<DockerPublisher> logger) : IPublisher
{
    private const string WorkerRunLabelKey = "foundry.worker-run-id";
    private const string ManagedLabelKey = "foundry.managed";

    /// <summary>Bounds a Prepare that hangs (a stalled provider), so the dispatch fails rather than waiting forever.</summary>
    private static readonly TimeSpan PrepareTimeout = TimeSpan.FromMinutes(15);

    /// <summary>Bounds a Publish that hangs; the attempt fails transiently and is retried with backoff.</summary>
    private static readonly TimeSpan PublishTimeout = TimeSpan.FromMinutes(15);

    public async Task<PrepareOutcome> PrepareAsync(PrepareRequest request, CancellationToken cancellationToken)
    {
        WorkerContainerSpec spec = PublisherContainerSpec.ForPrepare(request);
        string? containerId = null;

        try
        {
            await CreateHandOffVolumeAsync(request.RunId, cancellationToken);
            containerId = await runtime.CreateAndStartAsync(DockerWorkerOrchestrator.ToCreateParameters(spec), cancellationToken);

            long? exitCode = await WaitWithinTimeoutAsync(containerId, PrepareTimeout, cancellationToken);

            if (exitCode is null)
            {
                return new PrepareOutcome.Failed(new FailureReason.ContainerError(
                    $"The Publisher did not prepare the run's base within {PrepareTimeout.TotalMinutes:0} minutes."));
            }

            string? log = await runtime.GetAllLogsAsync(containerId, cancellationToken);
            return PrepareOutputParser.Parse(exitCode.Value, log, expectsRunBranch: request.RunBranch is not null);
        }
        catch (DockerApiException ex)
        {
            return new PrepareOutcome.Failed(new FailureReason.ContainerError(
                $"The Publisher could not run: {DockerWorkerOrchestrator.RedactDockerError(ex.Message)}"));
        }
        finally
        {
            if (containerId is not null)
            {
                await TryRemoveAsync(containerId);
            }
        }
    }

    public async Task<PublishResult> PublishAsync(PublishRequest request, CancellationToken cancellationToken)
    {
        WorkerContainerSpec spec = PublisherContainerSpec.ForPublish(request);
        string? containerId = null;

        try
        {
            containerId = await runtime.CreateAndStartAsync(DockerWorkerOrchestrator.ToCreateParameters(spec), cancellationToken);

            long? exitCode = await WaitWithinTimeoutAsync(containerId, PublishTimeout, cancellationToken);

            if (exitCode is null)
            {
                return new PublishResult.Failed(
                    $"The Publisher did not publish the run within {PublishTimeout.TotalMinutes:0} minutes.",
                    Transient: true);
            }

            string? log = await runtime.GetAllLogsAsync(containerId, cancellationToken);
            return PublishOutputParser.Parse(exitCode.Value, log);
        }
        catch (DockerApiException ex)
        {
            return new PublishResult.Failed(
                $"The Publisher could not run: {DockerWorkerOrchestrator.RedactDockerError(ex.Message)}",
                Transient: true);
        }
        finally
        {
            if (containerId is not null)
            {
                await TryRemoveAsync(containerId);
            }
        }
    }

    private async Task<long?> WaitWithinTimeoutAsync(string containerId, TimeSpan limit, CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(limit);

        try
        {
            return await runtime.WaitAsync(containerId, timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    // Same per-run pattern as the rootless daemon's data-root volume: labelled with the run ID so the orphan sweep
    // removes it once the run is neither starting nor active.
    private Task CreateHandOffVolumeAsync(WorkerRunId runId, CancellationToken cancellationToken) =>
        runtime.CreateVolumeAsync(
            new VolumesCreateParameters
            {
                Name = WorkerVolumeNames.HandOffVolumeName(runId),
                Labels = new Dictionary<string, string>
                {
                    [ManagedLabelKey] = "true",
                    [WorkerRunLabelKey] = runId.Value.ToString(),
                },
            },
            cancellationToken);

    // Removal also stops a Publisher that timed out, and deletes its anonymous scratch volumes; the Hand-off volume is
    // named, so it stays for the worker. It runs even when the dispatch was cancelled, so a token never outlives it.
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
                "Could not remove Publisher container {ContainerId}: {Error}",
                containerId,
                SecretRedactor.Redact(ex.Message));
        }
    }
}
