using System.Text;

using Docker.DotNet;
using Docker.DotNet.Models;

using Foundry.Modules.Credentials.Domain.ValueObjects;
using Foundry.Modules.Credentials.Features.Login;
using Foundry.Modules.Credentials.Infrastructure.Orchestration;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared.Infrastructure.Docker;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Credentials.Login;

/// <summary>
/// Integration tests for the login flow against the real login image
/// (<c>foundry-claude-login:local</c>, with the Claude CLI version pinned in
/// <c>workers/package.json</c>). They pin the CLI behaviours the login flow depends on
/// (ADR 0024 § CLI output coupling), through the production container command.
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

    // Long enough that the CLI's own timeout never ends a test early: URL wait plus the
    // longest post-delivery observation window.
    private const int LoginContainerTimeoutSeconds = 180;

    private static readonly TimeSpan UrlWait = TimeSpan.FromSeconds(90);

    private DockerClientConfiguration? _config;
    private DockerClient? _dockerClient;
    private CredentialsOrchestrator? _credentialsSut;
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

        DockerContainerRuntime runtime = new(
            _dockerClient.Containers,
            _dockerClient.Volumes,
            _dockerClient.Exec);

        _credentialsSut = new CredentialsOrchestrator(runtime);
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
    /// Starts the production login container and captures the OAuth URL from the CLI's stdout
    /// through the production log stream. Confirms that:
    /// (a) the container starts without blocking on interactive prompts,
    /// (b) the OAuth URL appears in the log stream within the timeout,
    /// (c) the URL matches the regex used by <see cref="AuthorizationUrlExtractor"/>.
    /// </summary>
    [Fact]
    public async Task StartLoginContainer_OAuthUrlCapturedFromRealCliStdout()
    {
        // Arrange
        Assert.SkipUnless(_daemonReachable, "Docker daemon is not reachable.");
        Assert.SkipUnless(_imagePresent, $"Login image '{LoginImage}' is not present. Build it first.");
        CredentialsOrchestrator credentialsSut = _credentialsSut.ShouldNotBeNull();
        await SeedVolumeAsync(_testVolumeName.ShouldNotBeNull());

        // Act
        string containerId = await StartProductionLoginContainerAsync(credentialsSut);

        try
        {
            using CancellationTokenSource urlCts = new(UrlWait);
            await using IAsyncEnumerator<string> lines =
                credentialsSut.StreamLogsAsync(containerId, urlCts.Token).GetAsyncEnumerator(urlCts.Token);

            string? capturedUrl = await ReadUntilAuthorizationUrlAsync(lines);

            // Assert — URL captured and is a valid OAuth URL
            capturedUrl.ShouldNotBeNull(
                $"OAuth URL was not emitted by the login container within {UrlWait.TotalSeconds} seconds.");
            capturedUrl.ShouldStartWith("https://");
            capturedUrl.ShouldContain("oauth");
        }
        finally
        {
            await ForceRemoveContainerAsync(containerId);
        }
    }

    /// <summary>
    /// Delivers a well-formed but bogus code through the production delivery path. The server
    /// rejects it, the CLI exits, and the log stream ends without the success line: this is the
    /// behaviour Foundry relies on to report <see cref="LoginFailureReason.InvalidCode"/>,
    /// without matching any CLI wording.
    ///
    /// NOT AUTOMATABLE: A successful token exchange (valid human-authorized code completing
    /// the OAuth round-trip) cannot be automated without a real browser session.
    /// That path is spike-verified and documented manually.
    /// </summary>
    [Fact]
    public async Task DeliverWellFormedBogusCode_StreamEndsWithoutSuccessSignal()
    {
        // Arrange
        Assert.SkipUnless(_daemonReachable, "Docker daemon is not reachable.");
        Assert.SkipUnless(_imagePresent, $"Login image '{LoginImage}' is not present. Build it first.");
        CredentialsOrchestrator credentialsSut = _credentialsSut.ShouldNotBeNull();
        await SeedVolumeAsync(_testVolumeName.ShouldNotBeNull());
        LoginCode bogusCode = LoginCode.Create("abcdefghijklmnop#qrstuvwxyz").ValueOrThrow();

        string containerId = await StartProductionLoginContainerAsync(credentialsSut);

        try
        {
            // One enumeration for the whole test, so the stream cannot miss the container's exit
            // (AutoRemove removes it as soon as it stops).
            using CancellationTokenSource streamCts = new(UrlWait);
            await using IAsyncEnumerator<string> lines =
                credentialsSut.StreamLogsAsync(containerId, streamCts.Token).GetAsyncEnumerator(streamCts.Token);

            (await ReadUntilAuthorizationUrlAsync(lines))
                .ShouldNotBeNull("OAuth URL must appear before delivering the code.");

            // Act
            await credentialsSut.DeliverLoginCodeAsync(containerId, bogusCode, TestContext.Current.CancellationToken);

            // The token exchange is a network round-trip to Anthropic's token endpoint.
            streamCts.CancelAfter(TimeSpan.FromSeconds(60));

            bool successSeen = false;
            while (await lines.MoveNextAsync())
            {
                if (lines.Current.Contains(LoginSessionService.LoginSuccessSignal, StringComparison.Ordinal))
                {
                    successSeen = true;
                }
            }

            // Assert — the runtime also ends the stream cleanly on cancellation, so the stream
            // ended on its own only if the 60-second token was not cancelled.
            streamCts.IsCancellationRequested.ShouldBeFalse(
                "The log stream must end on its own within 60 s of delivering a code the server rejects.");
            successSeen.ShouldBeFalse("A bogus code must never produce the success line.");
        }
        finally
        {
            await ForceRemoveContainerAsync(containerId);
        }
    }

    /// <summary>
    /// Delivers a code without <c>#</c> — a shape <see cref="LoginCode"/> refuses — straight
    /// to the CLI. The CLI refuses it locally and keeps waiting. This guards
    /// <see cref="LoginCode"/> against being stricter than the CLI: a CLI release that accepts
    /// such a code sends it to the server, which rejects it, the CLI exits, and this test fails.
    /// </summary>
    [Fact]
    public async Task DeliverCodeWithoutHash_CliKeepsWaiting()
    {
        // Arrange
        Assert.SkipUnless(_daemonReachable, "Docker daemon is not reachable.");
        Assert.SkipUnless(_imagePresent, $"Login image '{LoginImage}' is not present. Build it first.");
        CredentialsOrchestrator credentialsSut = _credentialsSut.ShouldNotBeNull();

        string containerId = await StartProductionLoginContainerAsync(credentialsSut);

        try
        {
            using (CancellationTokenSource urlCts = new(UrlWait))
            {
                await using IAsyncEnumerator<string> lines =
                    credentialsSut.StreamLogsAsync(containerId, urlCts.Token).GetAsyncEnumerator(urlCts.Token);

                (await ReadUntilAuthorizationUrlAsync(lines))
                    .ShouldNotBeNull("OAuth URL must appear before delivering the code.");
            }

            // Act — a raw string, because DeliverLoginCodeAsync accepts only a LoginCode.
            await ExecLoginCodeAsync(containerId, "INVALID-CODE-FOR-TEST");
            await Task.Delay(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);

            // Assert
            (await IsContainerRunningAsync(containerId)).ShouldBeTrue(
                "The CLI must refuse a code without '#' locally and keep waiting for another code.");
        }
        finally
        {
            await ForceRemoveContainerAsync(containerId);
        }
    }
    /// <summary>
    /// Verifies that <see cref="CredentialsOrchestrator.GetCredentialVolumeAuthStatusAsync"/> correctly
    /// parses the real <c>claude auth status --json</c> output framing (Docker exec stdout
    /// via real multiplexed stream). When the credential volume has no valid token, the
    /// CLI reports not-logged-in — this confirms the exec→parse path works end-to-end.
    /// </summary>
    [Fact]
    public async Task GetCredentialVolumeAuthStatusAsync_ParsesRealCliOutputFraming()
    {
        // Arrange
        Assert.SkipUnless(_daemonReachable, "Docker daemon is not reachable.");
        Assert.SkipUnless(_imagePresent, $"Login image '{LoginImage}' is not present. Build it first.");
        CredentialsOrchestrator credentialsSut = _credentialsSut.ShouldNotBeNull();
        string volumeName = _testVolumeName.ShouldNotBeNull();

        await SeedVolumeAsync(volumeName);

        // Act — exec auth status via the credential volume; no real token → loggedIn:false
        Foundry.Shared.Result<AccountIdentity> result =
            await credentialsSut.GetCredentialVolumeAuthStatusAsync(TestContext.Current.CancellationToken);

        // Assert — parse succeeds (no JSON framing artifacts); result is Failure (not logged in)
        // The key assertion is that Parse receives valid JSON (not Docker frame headers).
        result.ShouldBeOfType<Foundry.Shared.Result<AccountIdentity>.Failure>(
            "Auth status should fail because no valid OAuth token exists on the test volume.");
    }

    /// <summary>
    /// Verifies that <see cref="CredentialsOrchestrator.ListTransientContainersAsync"/>
    /// finds a running container labeled <c>foundry.transient=true</c> and returns its ID.
    /// This exercises the label-filter query that the reapers use.
    /// </summary>
    [Fact]
    public async Task ListTransientContainersAsync_ReturnsTransientLabeledContainers()
    {
        // Arrange
        Assert.SkipUnless(_daemonReachable, "Docker daemon is not reachable.");
        Assert.SkipUnless(_imagePresent, $"Login image '{LoginImage}' is not present. Build it first.");
        CredentialsOrchestrator credentialsSut = _credentialsSut.ShouldNotBeNull();
        string volumeName = _testVolumeName.ShouldNotBeNull();

        // Start a container with the transient label (simulates an orphaned login container).
        CreateContainerResponse response = await _dockerClient!.Containers.CreateContainerAsync(
            new CreateContainerParameters
            {
                Image = LoginImage,
                Cmd = ["sh", "-c", "sleep 30"],
                Labels = new Dictionary<string, string>
                {
                    ["foundry.transient"] = "true",
                    ["foundry.role"] = "login",
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
            IReadOnlyList<string> found =
                await credentialsSut.ListTransientContainersAsync(TestContext.Current.CancellationToken);

            // Assert — at least our test container appears in the result
            // Docker may return either the full ID or a short prefix.
            string containerPrefix = containerId[..12];
            found.ShouldContain(id => id == containerId || id.StartsWith(containerPrefix, StringComparison.Ordinal));
        }
        finally
        {
            await ForceRemoveContainerAsync(containerId);
        }
    }

    /// <summary>
    /// Verifies the reap behavior: a container labeled <c>foundry.transient=true</c>
    /// that is stopped and removed no longer
    /// appears in <see cref="CredentialsOrchestrator.ListTransientContainersAsync"/>.
    /// </summary>
    [Fact]
    public async Task StartupReap_RemovesOrphanedTransientContainer()
    {
        // Arrange
        Assert.SkipUnless(_daemonReachable, "Docker daemon is not reachable.");
        Assert.SkipUnless(_imagePresent, $"Login image '{LoginImage}' is not present. Build it first.");
        CredentialsOrchestrator credentialsSut = _credentialsSut.ShouldNotBeNull();
        string volumeName = _testVolumeName.ShouldNotBeNull();

        // Start an "orphaned" login container.
        CreateContainerResponse response = await _dockerClient!.Containers.CreateContainerAsync(
            new CreateContainerParameters
            {
                Image = LoginImage,
                Cmd = ["sh", "-c", "sleep 60"],
                Labels = new Dictionary<string, string>
                {
                    ["foundry.transient"] = "true",
                    ["foundry.role"] = "login",
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
        string orphanPrefix = orphanId[..12];
        IReadOnlyList<string> before =
            await credentialsSut.ListTransientContainersAsync(TestContext.Current.CancellationToken);
        before.ShouldContain(id => id == orphanId || id.StartsWith(orphanPrefix, StringComparison.Ordinal));

        // Act — simulate what the reapers do: stop then remove.
        await credentialsSut.StopContainerAsync(orphanId, TestContext.Current.CancellationToken);
        await credentialsSut.RemoveContainerAsync(orphanId, TestContext.Current.CancellationToken);

        // Assert — orphan no longer listed
        IReadOnlyList<string> after =
            await credentialsSut.ListTransientContainersAsync(TestContext.Current.CancellationToken);
        after.ShouldNotContain(id => id == orphanId || id.StartsWith(orphanPrefix, StringComparison.Ordinal));
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

    private static async Task<string> StartProductionLoginContainerAsync(CredentialsOrchestrator credentialsSut)
    {
        Foundry.Shared.Result<string> started = await credentialsSut.StartLoginContainerAsync(
            new LoginContainerSpec(LoginContainerTimeoutSeconds),
            TestContext.Current.CancellationToken);

        return started.ValueOrThrow();
    }

    private static async Task<string?> ReadUntilAuthorizationUrlAsync(IAsyncEnumerator<string> lines)
    {
        while (await lines.MoveNextAsync())
        {
            string? url = AuthorizationUrlExtractor.Extract(lines.Current);
            if (url is not null)
            {
                return url;
            }
        }

        return null;
    }

    private async Task ExecLoginCodeAsync(string containerId, string rawCode)
    {
        ContainerExecCreateResponse execResponse = await _dockerClient!.Exec.ExecCreateContainerAsync(
            containerId,
            new ContainerExecCreateParameters
            {
                Cmd = [.. LoginExecCommand.ForCode().Argv],
                Env = [$"C={rawCode}"],
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

    private async Task<bool> IsContainerRunningAsync(string containerId)
    {
        try
        {
            ContainerInspectResponse inspect = await _dockerClient!.Containers.InspectContainerAsync(
                containerId,
                TestContext.Current.CancellationToken);
            return inspect.State.Running;
        }
        catch (DockerContainerNotFoundException)
        {
            // AutoRemove deletes the login container as soon as it exits.
            return false;
        }
    }

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
