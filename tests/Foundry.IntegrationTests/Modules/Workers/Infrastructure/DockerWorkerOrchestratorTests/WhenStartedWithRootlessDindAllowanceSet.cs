using System.Net;

using Docker.DotNet;
using Docker.DotNet.Models;

using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features;
using Foundry.Modules.Workers.Features.ContainerSpec;
using Foundry.Modules.Workers.Infrastructure;
using Foundry.Shared;
using Foundry.Shared.Infrastructure.Docker;

using Microsoft.Extensions.Options;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Workers.Infrastructure.DockerWorkerOrchestratorTests;

/// <summary>
/// The unit tests run against a fake container runtime, so only a real daemon proves that the rootless
/// Docker-in-Docker allowance set is accepted and that the empty MaskedPaths/ReadonlyPaths survive
/// serialization instead of being replaced by the daemon's default masking.
/// These tests require a live Docker daemon; they skip cleanly when none is reachable.
/// </summary>
public sealed class WhenStartedWithRootlessDindAllowanceSet : IAsyncLifetime
{
    private const string Image = "busybox:latest";
    private const string WorkerRunIdLabel = "foundry.worker-run-id";

    private readonly WorkerRunId _runId = WorkerRunId.New();

    private DockerClientConfiguration? _config;
    private DockerClient? _dockerClient;
    private DockerWorkerOrchestrator? _sut;
    private string? _containerId;
    private bool _daemonReachable;

    async ValueTask IAsyncLifetime.InitializeAsync()
    {
        _config = new DockerClientConfiguration();
        _dockerClient = _config.CreateClient();

        _daemonReachable = await ProbeDaemonAsync();
        if (!_daemonReachable)
        {
            return;
        }

        await PullImageIfAbsentAsync();

        WorkerOptions options = new()
        {
            Image = Image,
            MemoryLimitMb = 512,
            CpuLimit = 1.0,
            PidsLimit = 256,
        };

        DockerContainerRuntime runtime = new(
            _dockerClient.Containers,
            _dockerClient.Volumes,
            _dockerClient.Exec);

        _sut = new DockerWorkerOrchestrator(runtime, Options.Create(options));
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        if (_dockerClient is null)
        {
            return;
        }

        try
        {
            await RemoveContainerAsync();
            await RemoveDataRootVolumeAsync();
        }
        finally
        {
            _dockerClient.Dispose();
            _config?.Dispose();
        }
    }

    [Fact]
    public async Task WhenStartedWithRootlessDindAllowanceSet_StartsAndLeavesSystemPathsUnmasked()
    {
        // Arrange
        Assert.SkipUnless(_daemonReachable, "Docker daemon is not reachable.");
        DockerWorkerOrchestrator sut = _sut.ShouldNotBeNull();
        WorkerContainerSpec spec = new(
            Image: Image,
            EnvironmentVariables: new Dictionary<string, string>(),
            BindMounts: [],
            Labels: new Dictionary<string, string> { [WorkerRunIdLabel] = _runId.Value.ToString() },
            Command: ["true"])
        {
            Capabilities = RootlessDindRunSpec.Capabilities,
            SecurityOptions = RootlessDindRunSpec.SecurityOptions,
            Devices = RootlessDindRunSpec.Devices,
            UnmaskSystemPaths = RootlessDindRunSpec.UnmaskSystemPaths,
            VolumeMounts =
            [
                new VolumeMount(WorkerVolumeNames.DataRootVolumeName(_runId), RootlessDindRunSpec.DataRootContainerPath),
            ],
        };

        // Act
        Result<ContainerId> result = await sut.StartAsync(spec, TestContext.Current.CancellationToken);

        // Assert
        string failureMessage = result is Result<ContainerId>.Failure failure
            ? $"{failure.Error.Code}: {failure.Error.Message}"
            : string.Empty;
        Result<ContainerId>.Success success = result.ShouldBeOfType<Result<ContainerId>.Success>(failureMessage);
        _containerId = success.Value.Value;

        ContainerInspectResponse inspect = await _dockerClient!.Containers.InspectContainerAsync(
            _containerId,
            TestContext.Current.CancellationToken);

        inspect.HostConfig.ShouldSatisfyAllConditions(
            () => inspect.HostConfig.MaskedPaths.ShouldNotBeNull().ShouldBeEmpty(),
            () => inspect.HostConfig.ReadonlyPaths.ShouldNotBeNull().ShouldBeEmpty());
    }

    private async Task RemoveContainerAsync()
    {
        if (_containerId is null)
        {
            return;
        }

        try
        {
            await _dockerClient!.Containers.RemoveContainerAsync(
                _containerId,
                new ContainerRemoveParameters { Force = true },
                CancellationToken.None);
        }
        catch (DockerContainerNotFoundException)
        {
            // Already removed — nothing to do.
        }
    }

    // The volume is created before the container, so a failed start would otherwise leak it.
    // It can only be removed after the container that mounts it.
    private async Task RemoveDataRootVolumeAsync()
    {
        try
        {
            await _dockerClient!.Volumes.RemoveAsync(
                WorkerVolumeNames.DataRootVolumeName(_runId),
                force: true,
                CancellationToken.None);
        }
        catch (DockerApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // Never created — nothing to do.
        }
    }

    private async Task<bool> ProbeDaemonAsync()
    {
        try
        {
            using CancellationTokenSource cts = new(TimeSpan.FromSeconds(3));
            await _dockerClient!.System.PingAsync(cts.Token);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (DockerApiException)
        {
            return false;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private async Task PullImageIfAbsentAsync()
    {
        await _dockerClient!.Images.CreateImageAsync(
            new ImagesCreateParameters
            {
                FromImage = "busybox",
                Tag = "latest",
            },
            authConfig: null,
            progress: new Progress<JSONMessage>(),
            cancellationToken: TestContext.Current.CancellationToken);
    }
}
