using Docker.DotNet;
using Docker.DotNet.Models;

using Foundry.Modules.Workers.Features;
using Foundry.Modules.Workers.Infrastructure;
using Foundry.Shared.Infrastructure.Docker;

using Microsoft.Extensions.Options;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Workers.Infrastructure.DockerWorkerOrchestratorTests;

/// <summary>
/// Integration tests proving that DockerWorkerOrchestrator correctly demuxes Docker's
/// 8-byte multiplexed frame headers (non-TTY mode) and redacts secrets end-to-end.
/// These tests require a live Docker daemon; they skip cleanly when none is reachable.
/// </summary>
public sealed class WhenContainerEmitsFramedLogsWithSecrets : IAsyncLifetime
{
    private const string Image = "busybox:latest";

    // Secrets embedded in the container command — real-shaped but not real credentials.
    private const string GitHubToken = "ghp_Xy7mN3kPqR8vT2wA1bL9cE6fH0jK5nD4";
    private const string GitLabPat = "glpat-SecretToken42";
    private const string GitLabUrl = "https://glpat-SecretToken42@gitlab.example.com/owner/repo.git";
    private const string CleanLine = "Build completed successfully.";

    // Docker multiplexed-frame stream header bytes (first byte of the 8-byte header).
    private const char StdoutFrameMarker = '\x01';
    private const char StderrFrameMarker = '\x02';

    private DockerClientConfiguration? _config;
    private DockerClient? _dockerClient;
    private string? _containerId;
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

        string containerName = $"foundry-integration-test-{Guid.NewGuid():N}";

        // The command echoes three lines: two with secrets and one clean line.
        // The container runs non-TTY (Tty = false) so Docker emits 8-byte multiplexed
        // frame headers — this is the framing path the unit stubs cannot exercise.
        string cmd = $"echo '{GitHubToken}' && echo '{GitLabUrl}' && echo '{CleanLine}'";

        CreateContainerResponse response = await _dockerClient.Containers.CreateContainerAsync(
            new CreateContainerParameters
            {
                Image = Image,
                Name = containerName,
                Cmd = ["/bin/sh", "-c", cmd],
                Tty = false,
                AttachStdout = true,
                AttachStderr = true,
            },
            TestContext.Current.CancellationToken);

        _containerId = response.ID;

        await _dockerClient.Containers.StartContainerAsync(
            _containerId,
            new ContainerStartParameters(),
            TestContext.Current.CancellationToken);

        // Wait for the container to exit so all output is flushed before GetLogsAsync runs.
        await _dockerClient.Containers.WaitContainerAsync(
            _containerId,
            TestContext.Current.CancellationToken);

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
            if (_containerId is not null)
            {
                try
                {
                    await _dockerClient.Containers.RemoveContainerAsync(
                        _containerId,
                        new ContainerRemoveParameters { Force = true },
                        CancellationToken.None);
                }
                catch (DockerContainerNotFoundException)
                {
                    // Already removed — nothing to do.
                }
            }
        }
        finally
        {
            _dockerClient.Dispose();
            _config?.Dispose();
        }
    }

    [Fact]
    public async Task WhenContainerEmitsFramedLogsWithSecrets_GetLogsAsync_SecretsRedactedAndDemuxed()
    {
        // Arrange
        Assert.SkipUnless(_daemonReachable, "Docker daemon is not reachable.");
        DockerWorkerOrchestrator sut = _sut.ShouldNotBeNull();

        // Act
        string? result = await sut.GetLogsAsync(
            _containerId.ShouldNotBeNull(),
            tailLines: 100,
            TestContext.Current.CancellationToken);

        // Assert
        result.ShouldNotBeNull();
        result.ShouldSatisfyAllConditions(
            () => result.ShouldNotContain(GitHubToken),
            () => result.ShouldNotContain(GitLabPat),
            () => result.ShouldContain("***"),
            () => result.ShouldContain(CleanLine),
            () => result.ShouldNotContain(StdoutFrameMarker),
            () => result.ShouldNotContain(StderrFrameMarker));
    }

    [Fact]
    public async Task WhenContainerEmitsFramedLogsWithSecrets_StreamLogsAsync_SecretsRedactedAndDemuxed()
    {
        // Arrange
        Assert.SkipUnless(_daemonReachable, "Docker daemon is not reachable.");
        DockerWorkerOrchestrator sut = _sut.ShouldNotBeNull();

        using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(30));

        // Act
        List<string> lines = [];
        await foreach (string line in sut.StreamLogsAsync(_containerId.ShouldNotBeNull(), cts.Token))
        {
            lines.Add(line);
        }

        // Assert
        lines.ShouldNotBeEmpty();
        lines.ShouldSatisfyAllConditions(
            () => lines.ShouldNotContain(GitHubToken),
            () => lines.ShouldAllBe(l => !l.Contains(GitLabPat)),
            () => lines.ShouldContain(l => l.Contains("***")),
            () => lines.ShouldContain(l => l.Contains(CleanLine)),
            () => lines.ShouldAllBe(l => !l.Contains(StdoutFrameMarker)),
            () => lines.ShouldAllBe(l => !l.Contains(StderrFrameMarker)));
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
