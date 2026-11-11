using Docker.DotNet;
using Docker.DotNet.Models;

using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.ContainerSpec;
using Foundry.Modules.Workers.Infrastructure;
using Foundry.Shared;
using Foundry.Shared.Infrastructure.Docker;

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
    // Alpine rather than busybox: like the worker image, it ships /tmp and /var/tmp as sticky 1777 directories, which
    // the anonymous volumes inherit. Busybox has no /var/tmp, so the daemon would create the mount point as 0755.
    private const string ImageRepository = "alpine";
    private const string ImageTag = "latest";
    private const string Image = $"{ImageRepository}:{ImageTag}";
    private const string WorkerRunIdLabel = "foundry.worker-run-id";
    private static readonly string[] TempDirectories = ["/tmp", "/var/tmp"];

    // Probes the temp directories as an unprivileged user, as the worker's node user would use them: it must be able
    // to write there and run what it wrote (the entrypoint's GIT_ASKPASS helper).
    private const string TempDirectoryProbe =
        "for p in /tmp /var/tmp; do "
        + "stat -c 'mode=%n:%a' \"$p\"; "
        + "awk -v p=\"$p\" '$2 == p { print \"fstype=\" p \":\" $3 }' /proc/mounts; "
        + "su -s /bin/sh nobody -c "
        + "\"f=\\$(mktemp -p $p) && printf '#!/bin/sh\\necho ok\\n' > \\$f && chmod 700 \\$f && \\$f\" >/dev/null "
        + "&& echo \"unprivileged-exec=$p\"; done";

    private readonly WorkerRunId _runId = WorkerRunId.New();

    private DockerClientConfiguration? _config;
    private DockerClient? _dockerClient;
    private DockerWorkerOrchestrator? _sut;
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

        DockerContainerRuntime runtime = new(
            _dockerClient.Containers,
            _dockerClient.Volumes,
            _dockerClient.Exec);

        _sut = new DockerWorkerOrchestrator(runtime);
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        if (_dockerClient is null)
        {
            return;
        }

        try
        {
            if (_daemonReachable)
            {
                await TryCleanupAsync(RemoveRunContainersAsync);
                await TryCleanupAsync(RemoveDataRootVolumeAsync);
            }
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
        WorkerContainerSpec spec = BuildDindSpec(["true"]);

        // Act
        Result<ContainerId> result = await sut.StartAsync(spec, TestContext.Current.CancellationToken);

        // Assert
        string failureMessage = result is Result<ContainerId>.Failure failure
            ? $"{failure.Error.Code}: {failure.Error.Message}"
            : string.Empty;
        Result<ContainerId>.Success success = result.ShouldBeOfType<Result<ContainerId>.Success>(failureMessage);

        ContainerInspectResponse inspect = await _dockerClient!.Containers.InspectContainerAsync(
            success.Value.Value,
            TestContext.Current.CancellationToken);

        inspect.HostConfig.ShouldSatisfyAllConditions(
            () => inspect.HostConfig.MaskedPaths.ShouldNotBeNull().ShouldBeEmpty(),
            () => inspect.HostConfig.ReadonlyPaths.ShouldNotBeNull().ShouldBeEmpty(),
            () => inspect.HostConfig.CapDrop.ShouldNotBeNull().ShouldContain(c => c.Equals("ALL", StringComparison.OrdinalIgnoreCase)),
            () => inspect.HostConfig.ReadonlyRootfs.ShouldBeTrue());
    }

    [Fact]
    public async Task WhenStartedWithRootlessDindAllowanceSet_TempDirectoriesAreStickyAnonymousVolumes()
    {
        // Arrange
        Assert.SkipUnless(_daemonReachable, "Docker daemon is not reachable.");
        DockerWorkerOrchestrator sut = _sut.ShouldNotBeNull();

        // Act
        string containerId = await StartAndWaitForExitAsync(sut, BuildDindSpec(["/bin/sh", "-c", TempDirectoryProbe]));
        string output = await sut.GetLogsAsync(containerId, tailLines: 100, TestContext.Current.CancellationToken) ?? string.Empty;

        // Assert
        ContainerInspectResponse inspect = await _dockerClient!.Containers.InspectContainerAsync(
            containerId,
            TestContext.Current.CancellationToken);
        string[] lines = [.. output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => DockerLogLine.StripTimestamp(l).Trim())];

        inspect.ShouldSatisfyAllConditions(
            [
                () => inspect.HostConfig.Tmpfs.ShouldNotBeNull().Keys.ShouldNotContain(k => TempDirectories.Contains(k)),
                () => inspect.Mounts
                    .Where(m => m.Type == "volume" && TempDirectories.Contains(m.Destination))
                    .Select(m => m.Destination)
                    .ShouldBe(TempDirectories, ignoreOrder: true),
                .. TempDirectories.SelectMany<string, Action>(p =>
                [
                    () => lines.ShouldContain($"mode={p}:1777", output),
                    () => lines.ShouldContain(l => l.StartsWith($"fstype={p}:", StringComparison.Ordinal), output),
                    () => lines.ShouldNotContain($"fstype={p}:tmpfs", output),
                    () => lines.ShouldContain($"unprivileged-exec={p}", output),
                ]),
            ]);
    }

    [Fact]
    public async Task WhenRemoved_TempDirectoryVolumesAreDeletedWithTheContainer()
    {
        // Arrange
        Assert.SkipUnless(_daemonReachable, "Docker daemon is not reachable.");
        DockerWorkerOrchestrator sut = _sut.ShouldNotBeNull();
        string containerId = await StartAndWaitForExitAsync(sut, BuildDindSpec(["true"]));
        ContainerInspectResponse inspect = await _dockerClient!.Containers.InspectContainerAsync(
            containerId,
            TestContext.Current.CancellationToken);
        List<string> tempVolumes = [.. inspect.Mounts
            .Where(m => m.Type == "volume" && TempDirectories.Contains(m.Destination))
            .Select(m => m.Name)];
        tempVolumes.Count.ShouldBe(TempDirectories.Length);

        // Act
        await sut.StopAndRemoveAsync(containerId, TestContext.Current.CancellationToken);

        // Assert
        foreach (string volume in tempVolumes)
        {
            await Should.ThrowAsync<DockerApiException>(
                () => _dockerClient.Volumes.InspectAsync(volume, TestContext.Current.CancellationToken));
        }
    }

    // Composed as IssueClaimedHandler composes it: the hardened baseline, then the DinD relaxations.
    private WorkerContainerSpec BuildDindSpec(IReadOnlyList<string> command) =>
        WorkerHardeningSpec.Apply(new(
            Image: Image,
            EnvironmentVariables: new Dictionary<string, string>(),
            Labels: new Dictionary<string, string> { [WorkerRunIdLabel] = _runId.Value.ToString() },
            Command: command)
        {
            MemoryLimitMb = 512,
            CpuLimit = 1.0,
            PidsLimit = 256,
        }) with
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

    private async Task<string> StartAndWaitForExitAsync(DockerWorkerOrchestrator sut, WorkerContainerSpec spec)
    {
        Result<ContainerId> result = await sut.StartAsync(spec, TestContext.Current.CancellationToken);
        string failureMessage = result is Result<ContainerId>.Failure failure
            ? $"{failure.Error.Code}: {failure.Error.Message}"
            : string.Empty;
        string containerId = result.ShouldBeOfType<Result<ContainerId>.Success>(failureMessage).Value.Value;

        await _dockerClient!.Containers.WaitContainerAsync(containerId, TestContext.Current.CancellationToken);

        return containerId;
    }

    // Found by label rather than by id: a container that was created but failed to start never
    // reports an id back to the test, yet would still hold the volume.
    private async Task RemoveRunContainersAsync()
    {
        IList<ContainerListResponse> containers = await _dockerClient!.Containers.ListContainersAsync(
            new ContainersListParameters
            {
                All = true,
                Filters = new Dictionary<string, IDictionary<string, bool>>
                {
                    ["label"] = new Dictionary<string, bool>
                    {
                        [$"{WorkerRunIdLabel}={_runId.Value}"] = true,
                    },
                },
            },
            CancellationToken.None);

        foreach (ContainerListResponse container in containers)
        {
            await TryCleanupAsync(() => _dockerClient.Containers.RemoveContainerAsync(
                container.ID,
                new ContainerRemoveParameters { Force = true, RemoveVolumes = true },
                CancellationToken.None));
        }
    }

    // The volume is created before the container, so a failed start would otherwise leak it.
    // It can only be removed after the container that mounts it.
    private async Task RemoveDataRootVolumeAsync()
    {
        await _dockerClient!.Volumes.RemoveAsync(
            WorkerVolumeNames.DataRootVolumeName(_runId),
            force: true,
            CancellationToken.None);
    }

    // Each step is guarded on its own so one failure cannot skip the remaining steps, and none can
    // mask the test outcome.
    private static async Task TryCleanupAsync(Func<Task> step)
    {
        try
        {
            await step();
        }
        catch (DockerApiException)
        {
            // Already removed, never created, or still referenced; nothing more to clean.
        }
        catch (HttpRequestException)
        {
            // Daemon went away mid-teardown.
        }
        catch (IOException)
        {
            // Pipe or socket to the daemon broke mid-teardown.
        }
        catch (TimeoutException)
        {
            // Daemon did not answer in time.
        }
        catch (OperationCanceledException)
        {
            // Request cancelled or timed out by the client.
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
        catch (IOException)
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
        try
        {
            await _dockerClient!.Images.InspectImageAsync(Image, TestContext.Current.CancellationToken);
            return;
        }
        catch (DockerImageNotFoundException)
        {
            // Not cached locally — pull below.
        }

        await _dockerClient!.Images.CreateImageAsync(
            new ImagesCreateParameters
            {
                FromImage = ImageRepository,
                Tag = ImageTag,
            },
            authConfig: null,
            progress: new Progress<JSONMessage>(),
            cancellationToken: TestContext.Current.CancellationToken);
    }
}
