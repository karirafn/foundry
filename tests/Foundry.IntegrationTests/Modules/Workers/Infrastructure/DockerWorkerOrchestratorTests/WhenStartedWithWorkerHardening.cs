using Docker.DotNet;
using Docker.DotNet.Models;

using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.ContainerSpec;
using Foundry.Modules.Workers.Features.Orchestration;
using Foundry.Modules.Workers.Infrastructure;
using Foundry.Shared;
using Foundry.Shared.Infrastructure.Docker;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Workers.Infrastructure.DockerWorkerOrchestratorTests;

/// <summary>
/// The unit tests only see the HostConfig Foundry sends, so only a real daemon proves that the hardened worker spec
/// (ADR 0088) is accepted and takes effect inside the container: capabilities dropped, no-new-privileges and the
/// default seccomp filter applied, the root filesystem read-only, and the writable mounts writable. It also proves
/// that removing the container deletes its anonymous scratch volumes.
/// These tests require a live Docker daemon; they skip cleanly when none is reachable.
/// </summary>
public sealed class WhenStartedWithWorkerHardening : IAsyncLifetime
{
    private const string Image = "busybox:latest";
    private const string WorkerRunIdLabel = "foundry.worker-run-id";

    // The probe runs as the container's root user, which is the strongest position the hardening must hold against:
    // root may still write the root-owned tmpfs and volumes, but not the image's root filesystem.
    private const string Probe =
        "grep -E '^(CapEff|NoNewPrivs|Seccomp):' /proc/self/status; "
        + "if touch /etc/foundry-probe 2>/dev/null; then echo rootfs=writable; else echo rootfs=readonly; fi; "
        + "for p in /tmp /var/tmp /etc/claude-code /workspace /home/node; do "
        + "if touch \"$p/foundry-probe\"; then echo \"writable=$p\"; fi; done";

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
            }
        }
        finally
        {
            _dockerClient.Dispose();
            _config?.Dispose();
        }
    }

    [Fact]
    public async Task WhenStartedWithWorkerHardening_DaemonAppliesTheHardenedHostConfig()
    {
        // Arrange
        Assert.SkipUnless(_daemonReachable, "Docker daemon is not reachable.");
        DockerWorkerOrchestrator sut = _sut.ShouldNotBeNull();

        // Act
        string containerId = await StartAndWaitForExitAsync(sut);

        // Assert
        ContainerInspectResponse inspect = await _dockerClient!.Containers.InspectContainerAsync(
            containerId,
            TestContext.Current.CancellationToken);

        inspect.HostConfig.ShouldSatisfyAllConditions(
            () => inspect.HostConfig.CapDrop.ShouldNotBeNull().ShouldContain(c => c.Equals("ALL", StringComparison.OrdinalIgnoreCase)),
            () => inspect.HostConfig.CapAdd.ShouldNotBeNull().Select(StripCapPrefix).ShouldBe(["SETUID", "SETGID"], ignoreOrder: true),
            () => inspect.HostConfig.SecurityOpt.ShouldNotBeNull().ShouldContain(o => o.StartsWith("no-new-privileges", StringComparison.Ordinal)),
            () => inspect.HostConfig.SecurityOpt.ShouldNotContain(o => o.StartsWith("seccomp=unconfined", StringComparison.Ordinal)),
            () => inspect.HostConfig.ReadonlyRootfs.ShouldBeTrue(),
            () => inspect.HostConfig.Tmpfs.ShouldNotBeNull().Keys.ShouldBe(WorkerHardeningSpec.TmpfsMounts.Keys, ignoreOrder: true));
    }

    [Fact]
    public async Task WhenStartedWithWorkerHardening_HardeningTakesEffectInsideTheContainer()
    {
        // Arrange
        Assert.SkipUnless(_daemonReachable, "Docker daemon is not reachable.");
        DockerWorkerOrchestrator sut = _sut.ShouldNotBeNull();

        // Act
        string containerId = await StartAndWaitForExitAsync(sut);
        string output = await sut.GetLogsAsync(containerId, tailLines: 100, TestContext.Current.CancellationToken) ?? string.Empty;

        // Assert
        string[] lines = [.. output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => DockerLogLine.StripTimestamp(l).Trim())];

        // SETUID and SETGID are the only capabilities left: bits 6 and 7.
        lines.ShouldSatisfyAllConditions(
            () => lines.ShouldContain(l => l.StartsWith("CapEff:", StringComparison.Ordinal) && l.EndsWith("00000000000000c0", StringComparison.Ordinal), output),
            () => lines.ShouldContain(l => l.StartsWith("NoNewPrivs:", StringComparison.Ordinal) && l.EndsWith('1'), output),
            () => lines.ShouldContain(l => l.StartsWith("Seccomp:", StringComparison.Ordinal) && l.EndsWith('2'), output),
            () => lines.ShouldContain("rootfs=readonly", output),
            () => lines.Where(l => l.StartsWith("writable=", StringComparison.Ordinal))
                .Select(l => l["writable=".Length..])
                .ShouldBe([.. WorkerHardeningSpec.TmpfsMounts.Keys, .. WorkerHardeningSpec.ScratchVolumePaths], ignoreOrder: true, output));
    }

    [Fact]
    public async Task WhenRemoved_ScratchVolumesAreDeletedWithTheContainer()
    {
        // Arrange
        Assert.SkipUnless(_daemonReachable, "Docker daemon is not reachable.");
        DockerWorkerOrchestrator sut = _sut.ShouldNotBeNull();
        string containerId = await StartAndWaitForExitAsync(sut);
        ContainerInspectResponse inspect = await _dockerClient!.Containers.InspectContainerAsync(
            containerId,
            TestContext.Current.CancellationToken);
        List<string> scratchVolumes = [.. inspect.Mounts
            .Where(m => m.Type == "volume" && WorkerHardeningSpec.ScratchVolumePaths.Contains(m.Destination))
            .Select(m => m.Name)];
        scratchVolumes.Count.ShouldBe(WorkerHardeningSpec.ScratchVolumePaths.Count);

        // Act
        await sut.StopAndRemoveAsync(containerId, TestContext.Current.CancellationToken);

        // Assert
        foreach (string volume in scratchVolumes)
        {
            await Should.ThrowAsync<DockerApiException>(
                () => _dockerClient.Volumes.InspectAsync(volume, TestContext.Current.CancellationToken));
        }
    }

    private static string StripCapPrefix(string capability) =>
        capability.StartsWith("CAP_", StringComparison.OrdinalIgnoreCase) ? capability[4..] : capability;

    private async Task<string> StartAndWaitForExitAsync(DockerWorkerOrchestrator sut)
    {
        WorkerContainerSpec spec = WorkerHardeningSpec.Apply(new(
            Image: Image,
            EnvironmentVariables: new Dictionary<string, string>(),
            Labels: new Dictionary<string, string> { [WorkerRunIdLabel] = _runId.Value.ToString() },
            Command: ["/bin/sh", "-c", Probe])
        {
            MemoryLimitMb = 512,
            CpuLimit = 1.0,
            PidsLimit = 256,
        });

        Result<ContainerId> result = await sut.StartAsync(spec, TestContext.Current.CancellationToken);
        string failureMessage = result is Result<ContainerId>.Failure failure
            ? $"{failure.Error.Code}: {failure.Error.Message}"
            : string.Empty;
        string containerId = result.ShouldBeOfType<Result<ContainerId>.Success>(failureMessage).Value.Value;

        await _dockerClient!.Containers.WaitContainerAsync(containerId, TestContext.Current.CancellationToken);
        WorkerStatusProbe probe = await sut.GetStatusAsync(containerId, TestContext.Current.CancellationToken);
        probe.ShouldBeOfType<WorkerStatusProbe.Available>().Status.ExitCode.ShouldBe(0);

        return containerId;
    }

    // Found by label rather than by id: a container that was created but failed to start never
    // reports an id back to the test, yet would still hold its anonymous volumes.
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
            // Already removed or never created; nothing more to clean.
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
                FromImage = "busybox",
                Tag = "latest",
            },
            authConfig: null,
            progress: new Progress<JSONMessage>(),
            cancellationToken: TestContext.Current.CancellationToken);
    }
}
