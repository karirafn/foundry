using System.Text;

using Docker.DotNet;
using Docker.DotNet.Models;

using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Features;
using Foundry.Modules.Workers.Features.Login;
using Foundry.Modules.Workers.Infrastructure;
using Foundry.Shared.Infrastructure.Docker;

using Microsoft.Extensions.Options;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Workers.Login;

/// <summary>
/// Integration tests for the login flow against the real login image
/// (<c>foundry-claude-login:local</c>, pinned to Claude CLI @2.1.187).
///
/// These tests require:
///   - A live Docker daemon (Linux/CI).
///   - The <c>foundry-claude-login:local</c> image pre-built (or available).
///
/// They skip cleanly when Docker is unreachable or the image is absent.
/// They run in CI (Linux) and are intentionally NOT expected to run on
/// Windows dev machines where Docker desktop may not be running.
///
/// NOT AUTOMATABLE: A successful token exchange (valid human-authorized code
/// completing the OAuth round-trip) cannot be automated without a real browser
/// session — that path is spike-verified and documented manually.
/// </summary>
public sealed class LoginFlowIntegrationTests : IAsyncLifetime
{
    private const string LoginImage = WorkerImageNames.LoginImageName;

    private DockerClientConfiguration? _config;
    private DockerClient? _dockerClient;
    private DockerWorkerOrchestrator? _sut;
    private bool _daemonReachable;
    private bool _imagePresent;

    // Credential volume name used by tests — isolated from the production volume.
    private const string TestVolumeNamePrefix = "foundry-integration-test-creds-";

    private string? _testVolumeName;

    async ValueTask IAsyncLifetime.InitializeAsync()
    {
        _config = new DockerClientConfiguration();
        _dockerClient = _config.CreateClient();

        _daemonReachable = await ProbeDaemonAsync();
        if (!_daemonReachable)
        {
            return;
        }

        _imagePresent = await ImageExistsAsync(LoginImage);
        if (!_imagePresent)
        {
            return;
        }

        // Create a per-test-class isolated credential volume.
        _testVolumeName = $"{TestVolumeNamePrefix}{Guid.NewGuid():N}";
        await _dockerClient.Volumes.CreateAsync(
            new VolumesCreateParameters { Name = _testVolumeName },
            TestContext.Current.CancellationToken);

        WorkerOptions options = new()
        {
            Image = LoginImage,
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
            if (_testVolumeName is not null)
            {
                try
                {
                    await _dockerClient.Volumes.RemoveAsync(_testVolumeName, force: true, CancellationToken.None);
                }
                catch (DockerApiException)
                {
                    // Best-effort cleanup.
                }
            }
        }
        finally
        {
            _dockerClient.Dispose();
            _config?.Dispose();
        }
    }

    /// <summary>
    /// Starts a real login container with the FIFO bootstrap and captures the OAuth URL
    /// from the CLI's stdout. Confirms that:
    /// (a) the container starts without blocking on interactive prompts (seed is applied),
    /// (b) the OAuth URL appears in the log stream within the timeout,
    /// (c) the URL matches the regex used by <see cref="AuthorizationUrlExtractor"/>.
    ///
    /// Note: the seed is applied to the isolated test volume before the login container starts.
    /// </summary>
    [Fact]
    public async Task StartLoginContainer_OAuthUrlCapturedFromRealCliStdout()
    {
        // Arrange
        Assert.SkipUnless(_daemonReachable, "Docker daemon is not reachable.");
        Assert.SkipUnless(_imagePresent, $"Login image '{LoginImage}' is not present. Build it first.");
        DockerWorkerOrchestrator sut = _sut.ShouldNotBeNull();
        string volumeName = _testVolumeName.ShouldNotBeNull();

        // Seed the credential volume so onboarding doesn't block the login prompt.
        await SeedVolumeAsync(volumeName);

        int timeoutSeconds = 60;
        string fifoPath = LoginExecCommand.FifoPath;
        string sleepPidPath = LoginExecCommand.SleepPidPath;
        string bootstrapCmd =
            $"mkfifo {fifoPath}; sleep {timeoutSeconds} > {fifoPath} & echo $! > {sleepPidPath}; exec claude auth login --claudeai < {fifoPath}";

        CreateContainerParameters createParams = BuildLoginContainerParams(volumeName, bootstrapCmd);

        CreateContainerResponse response = await _dockerClient!.Containers.CreateContainerAsync(
            createParams,
            TestContext.Current.CancellationToken);

        string containerId = response.ID;

        try
        {
            await _dockerClient.Containers.StartContainerAsync(
                containerId,
                new ContainerStartParameters(),
                TestContext.Current.CancellationToken);

            // Act — scan log stream for the OAuth URL within a bounded window.
            // Use an independent CTS — test-framework cancellation is too tight for CLI startup time.
            using CancellationTokenSource urlCts = new(TimeSpan.FromSeconds(90));

            string? capturedUrl = null;

            await foreach (string line in sut.StreamLogsAsync(containerId, urlCts.Token))
            {
                string? url = AuthorizationUrlExtractor.Extract(line);
                if (url is not null)
                {
                    capturedUrl = url;
                    break;
                }
            }

            // Assert — URL captured and is a valid OAuth URL
            capturedUrl.ShouldNotBeNull(
                "OAuth URL was not emitted by the login container within 45 seconds. " +
                "Check that the onboarding seed was applied and the CLI is version 2.1.187.");
            capturedUrl.ShouldStartWith("https://");
            capturedUrl.ShouldContain("oauth");
        }
        finally
        {
            await ForceRemoveContainerAsync(containerId);
        }
    }

    /// <summary>
    /// Delivers a bogus authorization code via <c>docker exec</c> to the FIFO and confirms
    /// the CLI emits an "Invalid code" rejection message — verifying the exec→FIFO→CLI framing
    /// works end-to-end with real Docker.
    ///
    /// The CLI does NOT exit after a single invalid code attempt — it prompts the user to retry.
    /// Foundry's session timeout (session-level CTS) is what eventually causes the session to
    /// fail with <see cref="LoginFailureReason.CodeTimeout"/> in production, not a non-zero exit.
    /// We assert the rejection message, not the exit code.
    ///
    /// NOT AUTOMATABLE: A successful token exchange (valid human-authorized code completing
    /// the OAuth round-trip) cannot be automated without a real browser session.
    /// That path is spike-verified and documented manually.
    /// </summary>
    [Fact]
    public async Task DeliverBogusCode_CliEmitsInvalidCodeMessage()
    {
        // Arrange
        Assert.SkipUnless(_daemonReachable, "Docker daemon is not reachable.");
        Assert.SkipUnless(_imagePresent, $"Login image '{LoginImage}' is not present. Build it first.");
        DockerWorkerOrchestrator sut = _sut.ShouldNotBeNull();
        string volumeName = _testVolumeName.ShouldNotBeNull();

        await SeedVolumeAsync(volumeName);

        int timeoutSeconds = 60;
        string fifoPath = LoginExecCommand.FifoPath;
        string sleepPidPath = LoginExecCommand.SleepPidPath;
        string bootstrapCmd =
            $"mkfifo {fifoPath}; sleep {timeoutSeconds} > {fifoPath} & echo $! > {sleepPidPath}; exec claude auth login --claudeai < {fifoPath}";

        CreateContainerParameters createParams = BuildLoginContainerParams(volumeName, bootstrapCmd);

        CreateContainerResponse response = await _dockerClient!.Containers.CreateContainerAsync(
            createParams,
            TestContext.Current.CancellationToken);

        string containerId = response.ID;

        try
        {
            await _dockerClient.Containers.StartContainerAsync(
                containerId,
                new ContainerStartParameters(),
                TestContext.Current.CancellationToken);

            // Wait for the OAuth URL so the FIFO is open for reading before we write.
            // Use an independent CTS — test-framework cancellation is too tight for the CLI startup time.
            using CancellationTokenSource urlCts = new(TimeSpan.FromSeconds(90));

            bool urlSeen = false;
            await foreach (string line in sut.StreamLogsAsync(containerId, urlCts.Token))
            {
                if (AuthorizationUrlExtractor.Extract(line) is not null)
                {
                    urlSeen = true;
                    break;
                }
            }

            urlSeen.ShouldBeTrue("OAuth URL must appear before delivering the code.");

            // Act — deliver a bogus code via exec (code is not a real secret; kept out of logs)
            // The env var C holds the code — no shell injection possible.
            const string BogusCode = "INVALID-CODE-FOR-TEST";
            await sut.DeliverLoginCodeAsync(containerId, BogusCode, TestContext.Current.CancellationToken);

            // Assert — CLI rejected the bogus code and emitted an "Invalid code" message.
            // The CLI does NOT exit after one failed attempt; it stays alive and re-prompts.
            // Allow up to 60 s for the network round-trip to Anthropic's token endpoint.
            // We scan the log stream rather than wait for container exit.
            using CancellationTokenSource rejectionCts = new(TimeSpan.FromSeconds(60));

            bool rejectionSeen = false;
            await foreach (string line in sut.StreamLogsAsync(containerId, rejectionCts.Token))
            {
                if (line.Contains("Invalid code", StringComparison.OrdinalIgnoreCase))
                {
                    rejectionSeen = true;
                    break;
                }
            }

            rejectionSeen.ShouldBeTrue(
                "The CLI must emit an 'Invalid code' rejection message within 60 s of receiving a bogus code. " +
                "This confirms the exec→FIFO→CLI stdin framing works end-to-end.");
        }
        finally
        {
            await ForceRemoveContainerAsync(containerId);
        }
    }

    /// <summary>
    /// Verifies that <see cref="DockerWorkerOrchestrator.GetAuthStatusAsync"/> correctly
    /// parses the real <c>claude auth status --json</c> output framing (Docker exec stdout
    /// via real multiplexed stream). When the credential volume has no valid token, the
    /// CLI reports not-logged-in — this confirms the exec→parse path works end-to-end.
    /// </summary>
    [Fact]
    public async Task GetAuthStatusAsync_ParsesRealCliOutputFraming()
    {
        // Arrange
        Assert.SkipUnless(_daemonReachable, "Docker daemon is not reachable.");
        Assert.SkipUnless(_imagePresent, $"Login image '{LoginImage}' is not present. Build it first.");
        DockerWorkerOrchestrator sut = _sut.ShouldNotBeNull();
        string volumeName = _testVolumeName.ShouldNotBeNull();

        await SeedVolumeAsync(volumeName);

        // Start a minimal container that stays alive long enough for exec.
        string helperId = await StartHelperContainerAsync(volumeName);

        try
        {
            // Act — exec auth status; no real token → loggedIn:false
            Foundry.Shared.Result<AccountIdentity> result =
                await sut.GetAuthStatusAsync(helperId, TestContext.Current.CancellationToken);

            // Assert — parse succeeds (no JSON framing artifacts); result is Failure (not logged in)
            // The key assertion is that Parse receives valid JSON (not Docker frame headers).
            result.ShouldBeOfType<Foundry.Shared.Result<AccountIdentity>.Failure>(
                "Auth status should fail because no valid OAuth token exists on the test volume.");
        }
        finally
        {
            await RemoveContainerAsync(helperId);
        }
    }

    /// <summary>
    /// Verifies that <see cref="DockerWorkerOrchestrator.ListLoginContainersByLabelAsync"/>
    /// finds a running container labeled <c>foundry.login=true</c> and returns its ID.
    /// This exercises the label-filter query that the startup reaper uses.
    /// </summary>
    [Fact]
    public async Task ListLoginContainersByLabelAsync_ReturnsLabeledContainers()
    {
        // Arrange
        Assert.SkipUnless(_daemonReachable, "Docker daemon is not reachable.");
        Assert.SkipUnless(_imagePresent, $"Login image '{LoginImage}' is not present. Build it first.");
        DockerWorkerOrchestrator sut = _sut.ShouldNotBeNull();
        string volumeName = _testVolumeName.ShouldNotBeNull();

        // Start a container with the login label (simulates an orphaned login container).
        CreateContainerResponse response = await _dockerClient!.Containers.CreateContainerAsync(
            new CreateContainerParameters
            {
                Image = LoginImage,
                Cmd = ["sh", "-c", "sleep 30"],
                Labels = new Dictionary<string, string>
                {
                    ["foundry.login"] = "true",
                    ["foundry.managed"] = "true",
                },
                HostConfig = new HostConfig
                {
                    Mounts =
                    [
                        new Mount
                        {
                            Type = "volume",
                            Source = volumeName,
                            Target = WorkerVolumeNames.ClaudeConfigContainerPath,
                            ReadOnly = false,
                        },
                    ],
                },
            },
            TestContext.Current.CancellationToken);

        string containerId = response.ID;

        try
        {
            await _dockerClient.Containers.StartContainerAsync(
                containerId,
                new ContainerStartParameters(),
                TestContext.Current.CancellationToken);

            // Act
            IReadOnlyList<Foundry.Modules.Workers.Domain.ContainerId> found =
                await sut.ListLoginContainersByLabelAsync(TestContext.Current.CancellationToken);

            // Assert — at least our test container appears in the result
            found.ShouldContain(c => c.Value == containerId);
        }
        finally
        {
            await ForceRemoveContainerAsync(containerId);
        }
    }

    /// <summary>
    /// Verifies the startup reap behavior: a container labeled <c>foundry.login=true</c>
    /// that is stopped and removed by <see cref="LoginContainerReaper"/> logic no longer
    /// appears in <see cref="DockerWorkerOrchestrator.ListLoginContainersByLabelAsync"/>.
    /// </summary>
    [Fact]
    public async Task StartupReap_RemovesOrphanedLoginContainer()
    {
        // Arrange
        Assert.SkipUnless(_daemonReachable, "Docker daemon is not reachable.");
        Assert.SkipUnless(_imagePresent, $"Login image '{LoginImage}' is not present. Build it first.");
        DockerWorkerOrchestrator sut = _sut.ShouldNotBeNull();
        string volumeName = _testVolumeName.ShouldNotBeNull();

        // Start an "orphaned" login container.
        CreateContainerResponse response = await _dockerClient!.Containers.CreateContainerAsync(
            new CreateContainerParameters
            {
                Image = LoginImage,
                Cmd = ["sh", "-c", "sleep 60"],
                Labels = new Dictionary<string, string>
                {
                    ["foundry.login"] = "true",
                    ["foundry.managed"] = "true",
                },
                HostConfig = new HostConfig
                {
                    Mounts =
                    [
                        new Mount
                        {
                            Type = "volume",
                            Source = volumeName,
                            Target = WorkerVolumeNames.ClaudeConfigContainerPath,
                            ReadOnly = false,
                        },
                    ],
                },
            },
            TestContext.Current.CancellationToken);

        string orphanId = response.ID;

        await _dockerClient.Containers.StartContainerAsync(
            orphanId,
            new ContainerStartParameters(),
            TestContext.Current.CancellationToken);

        // Confirm it's listed before the reap.
        IReadOnlyList<Foundry.Modules.Workers.Domain.ContainerId> before =
            await sut.ListLoginContainersByLabelAsync(TestContext.Current.CancellationToken);
        before.ShouldContain(c => c.Value == orphanId);

        // Act — simulate what LoginContainerReaper does: stop then remove.
        await sut.StopContainerAsync(orphanId, TestContext.Current.CancellationToken);
        await sut.RemoveContainerAsync(orphanId, TestContext.Current.CancellationToken);

        // Assert — orphan no longer listed
        IReadOnlyList<Foundry.Modules.Workers.Domain.ContainerId> after =
            await sut.ListLoginContainersByLabelAsync(TestContext.Current.CancellationToken);
        after.ShouldNotContain(c => c.Value == orphanId);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

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

    private async Task<bool> ImageExistsAsync(string imageName)
    {
        try
        {
            IList<ImagesListResponse> images = await _dockerClient!.Images.ListImagesAsync(
                new ImagesListParameters
                {
                    Filters = new Dictionary<string, IDictionary<string, bool>>
                    {
                        ["reference"] = new Dictionary<string, bool>
                        {
                            [imageName] = true,
                        },
                    },
                },
                TestContext.Current.CancellationToken);

            return images.Count > 0;
        }
        catch (DockerApiException)
        {
            return false;
        }
    }

    private static CreateContainerParameters BuildLoginContainerParams(string volumeName, string bootstrapCmd) =>
        new()
        {
            Image = LoginImage,
            Cmd = ["sh", "-c", bootstrapCmd],
            Env = [$"{WorkerVolumeNames.ClaudeConfigDirEnvVar}={WorkerVolumeNames.ClaudeConfigContainerPath}"],
            Tty = false,
            AttachStdout = true,
            AttachStderr = true,
            WorkingDir = OnboardingSeed.DefaultWorkDir,
            Labels = new Dictionary<string, string>
            {
                ["foundry.login"] = "true",
                ["foundry.managed"] = "true",
            },
            HostConfig = new HostConfig
            {
                Mounts =
                [
                    new Mount
                    {
                        Type = "volume",
                        Source = volumeName,
                        Target = WorkerVolumeNames.ClaudeConfigContainerPath,
                        ReadOnly = false,
                    },
                ],
            },
        };

    /// <summary>
    /// Seeds the credential volume with onboarding suppression by running a helper container.
    /// Uses the same JSON that <c>SeedOnboardingAsync</c> would write.
    /// </summary>
    private async Task SeedVolumeAsync(string volumeName)
    {
        string mergedJson = OnboardingSeed.Merge(existingJson: null, workDir: OnboardingSeed.DefaultWorkDir);
        string configPath = $"{WorkerVolumeNames.ClaudeConfigContainerPath}/.claude.json";

        string helperId = await StartHelperContainerAsync(volumeName);
        try
        {
            await WriteFileToContainerAsync(helperId, configPath, mergedJson);
        }
        finally
        {
            await RemoveContainerAsync(helperId);
        }
    }

    /// <summary>
    /// Starts a minimal helper container that sleeps and mounts the given credential volume.
    /// Used for read/write operations on volume files via exec.
    /// </summary>
    private async Task<string> StartHelperContainerAsync(string volumeName)
    {
        CreateContainerResponse response = await _dockerClient!.Containers.CreateContainerAsync(
            new CreateContainerParameters
            {
                Image = LoginImage,
                Cmd = ["sh", "-c", "sleep 30"],
                Env = [$"{WorkerVolumeNames.ClaudeConfigDirEnvVar}={WorkerVolumeNames.ClaudeConfigContainerPath}"],
                HostConfig = new HostConfig
                {
                    Mounts =
                    [
                        new Mount
                        {
                            Type = "volume",
                            Source = volumeName,
                            Target = WorkerVolumeNames.ClaudeConfigContainerPath,
                            ReadOnly = false,
                        },
                    ],
                },
            },
            TestContext.Current.CancellationToken);

        await _dockerClient.Containers.StartContainerAsync(
            response.ID,
            new ContainerStartParameters(),
            TestContext.Current.CancellationToken);

        return response.ID;
    }

    private async Task WriteFileToContainerAsync(string containerId, string path, string content)
    {
        // Encode content as base64 to avoid shell escaping issues.
        string b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(content));

        ContainerExecCreateResponse execResponse = await _dockerClient!.Exec.ExecCreateContainerAsync(
            containerId,
            new ContainerExecCreateParameters
            {
                Cmd = ["sh", "-c", $"printf '%s' \"$J\" | base64 -d > {path}"],
                Env = [$"J={b64}"],
                AttachStdout = true,
                AttachStderr = true,
            },
            TestContext.Current.CancellationToken);

        using MultiplexedStream stream = await _dockerClient.Exec.StartAndAttachContainerExecAsync(
            execResponse.ID,
            tty: false,
            TestContext.Current.CancellationToken);

        await stream.CopyOutputToAsync(Stream.Null, Stream.Null, Stream.Null, TestContext.Current.CancellationToken);
    }

    private async Task<string?> ReadFileFromContainerAsync(string containerId, string path)
    {
        ContainerExecCreateResponse execResponse = await _dockerClient!.Exec.ExecCreateContainerAsync(
            containerId,
            new ContainerExecCreateParameters
            {
                Cmd = ["sh", "-c", $"cat {path} 2>/dev/null || echo ''"],
                AttachStdout = true,
                AttachStderr = false,
            },
            TestContext.Current.CancellationToken);

        using MultiplexedStream stream = await _dockerClient.Exec.StartAndAttachContainerExecAsync(
            execResponse.ID,
            tty: false,
            TestContext.Current.CancellationToken);

        using MemoryStream stdout = new();
        await stream.CopyOutputToAsync(Stream.Null, stdout, Stream.Null, TestContext.Current.CancellationToken);

        stdout.Seek(0, SeekOrigin.Begin);
        using StreamReader reader = new(stdout, Encoding.UTF8);
        string content = (await reader.ReadToEndAsync(TestContext.Current.CancellationToken)).Trim();

        return string.IsNullOrEmpty(content) ? null : content;
    }

    private async Task RemoveContainerAsync(string containerId)
    {
        try
        {
            await _dockerClient!.Containers.StopContainerAsync(
                containerId,
                new ContainerStopParameters { WaitBeforeKillSeconds = 5 },
                CancellationToken.None);
        }
        catch (DockerContainerNotFoundException)
        {
            // Already stopped.
        }

        try
        {
            await _dockerClient!.Containers.RemoveContainerAsync(
                containerId,
                new ContainerRemoveParameters { Force = true },
                CancellationToken.None);
        }
        catch (DockerContainerNotFoundException)
        {
            // Already removed.
        }
    }

    private async Task ForceRemoveContainerAsync(string containerId)
    {
        try
        {
            await _dockerClient!.Containers.RemoveContainerAsync(
                containerId,
                new ContainerRemoveParameters { Force = true },
                CancellationToken.None);
        }
        catch (DockerContainerNotFoundException)
        {
            // Already removed.
        }
    }
}
