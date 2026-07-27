using System.Net;

using Docker.DotNet;
using Docker.DotNet.Models;

using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features;
using Foundry.Modules.Workers.Features.Orchestration;
using Foundry.Modules.Workers.Features.ContainerSpec;
using Foundry.Modules.Workers.Infrastructure;
using Foundry.Shared;
using Foundry.UnitTests.Fakes.Workers;

using Microsoft.Extensions.Options;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Infrastructure.DockerWorkerOrchestratorTests;

public sealed class StartAsync
{
    private const long BytesPerMegabyte = 1024L * 1024L;
    private const long NanoCpusPerCpu = 1_000_000_000L;

    private static WorkerOptions DefaultOptions() => new()
    {
        Image = "test-image:latest",
        MemoryLimitMb = 512,
        CpuLimit = 1.5,
        PidsLimit = 256,
    };

    private static WorkerContainerSpec MinimalSpec(
        string image = "test-image:latest",
        IReadOnlyList<BindMount>? bindMounts = null,
        IReadOnlyList<string>? securityOptions = null,
        IReadOnlyList<string>? devices = null,
        IReadOnlyList<VolumeMount>? volumeMounts = null) =>
        new(
            Image: image,
            EnvironmentVariables: new Dictionary<string, string>(),
            BindMounts: bindMounts ?? [],
            Labels: new Dictionary<string, string>(),
            Command: [])
        {
            SecurityOptions = securityOptions ?? [],
            Devices = devices ?? [],
            VolumeMounts = volumeMounts ?? [],
        };

    private static DockerWorkerOrchestrator BuildSut(
        FakeDockerContainerRuntime runtime,
        WorkerOptions? options = null) =>
        new(runtime, Options.Create(options ?? DefaultOptions()));

    [Fact]
    public async Task WhenStarted_SetsMemoryLimitFromOptions()
    {
        // Arrange
        FakeDockerContainerRuntime runtime = new();
        WorkerOptions options = DefaultOptions();
        DockerWorkerOrchestrator sut = BuildSut(runtime, options);

        // Act
        await sut.StartAsync(MinimalSpec(), CancellationToken.None);

        // Assert
        CreateContainerParameters captured = runtime.LastCreateAndStartParameters.ShouldNotBeNull();
        captured.HostConfig.Memory.ShouldBe(options.MemoryLimitMb * BytesPerMegabyte);
    }

    [Fact]
    public async Task WhenStarted_SetsNanoCpusFromOptions()
    {
        // Arrange
        FakeDockerContainerRuntime runtime = new();
        WorkerOptions options = DefaultOptions();
        DockerWorkerOrchestrator sut = BuildSut(runtime, options);

        // Act
        await sut.StartAsync(MinimalSpec(), CancellationToken.None);

        // Assert
        CreateContainerParameters captured = runtime.LastCreateAndStartParameters.ShouldNotBeNull();
        captured.HostConfig.NanoCPUs.ShouldBe((long)(options.CpuLimit * NanoCpusPerCpu));
    }

    [Fact]
    public async Task WhenStarted_SetsPidsLimitFromOptions()
    {
        // Arrange
        FakeDockerContainerRuntime runtime = new();
        WorkerOptions options = DefaultOptions();
        DockerWorkerOrchestrator sut = BuildSut(runtime, options);

        // Act
        await sut.StartAsync(MinimalSpec(), CancellationToken.None);

        // Assert
        CreateContainerParameters captured = runtime.LastCreateAndStartParameters.ShouldNotBeNull();
        captured.HostConfig.PidsLimit.ShouldBe(options.PidsLimit);
    }

    [Fact]
    public async Task WhenStarted_SetsBindsFromSpec()
    {
        // Arrange
        FakeDockerContainerRuntime runtime = new();
        IReadOnlyList<BindMount> bindMounts =
        [
            new BindMount("/host/data", "/container/data", ReadOnly: false),
            new BindMount("/host/config", "/container/config", ReadOnly: true),
        ];
        DockerWorkerOrchestrator sut = BuildSut(runtime);

        // Act
        await sut.StartAsync(MinimalSpec(bindMounts: bindMounts), CancellationToken.None);

        // Assert
        CreateContainerParameters captured = runtime.LastCreateAndStartParameters.ShouldNotBeNull();
        captured.HostConfig.Binds.ShouldBe(
            ["/host/data:/container/data", "/host/config:/container/config:ro"],
            ignoreOrder: false);
    }

    [Fact]
    public async Task WhenStarted_ReturnsContainerIdOnSuccess()
    {
        // Arrange
        FakeDockerContainerRuntime runtime = new FakeDockerContainerRuntime()
            .WithContainerId("created-container-abc");
        DockerWorkerOrchestrator sut = BuildSut(runtime);

        // Act
        Result<ContainerId> result = await sut.StartAsync(MinimalSpec(), CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<ContainerId>.Success success = result.ShouldBeOfType<Result<ContainerId>.Success>();
        success.Value.Value.ShouldBe("created-container-abc");
    }

    [Fact]
    public async Task WhenSpecHasSecurityOptions_SetsHostConfigSecurityOpt()
    {
        // Arrange
        FakeDockerContainerRuntime runtime = new();
        IReadOnlyList<string> securityOptions = ["seccomp=unconfined", "apparmor=unconfined"];
        DockerWorkerOrchestrator sut = BuildSut(runtime);

        // Act
        await sut.StartAsync(MinimalSpec(securityOptions: securityOptions), CancellationToken.None);

        // Assert
        CreateContainerParameters captured = runtime.LastCreateAndStartParameters.ShouldNotBeNull();
        captured.HostConfig.SecurityOpt.ShouldBe(
            ["seccomp=unconfined", "apparmor=unconfined"],
            ignoreOrder: false);
    }

    [Fact]
    public async Task WhenSpecHasDevices_SetsHostConfigDevicesWithCorrectMapping()
    {
        // Arrange
        FakeDockerContainerRuntime runtime = new();
        IReadOnlyList<string> devices = ["/dev/fuse"];
        DockerWorkerOrchestrator sut = BuildSut(runtime);

        // Act
        await sut.StartAsync(MinimalSpec(devices: devices), CancellationToken.None);

        // Assert
        CreateContainerParameters captured = runtime.LastCreateAndStartParameters.ShouldNotBeNull();
        IList<DeviceMapping> hostDevices = captured.HostConfig.Devices.ShouldNotBeNull();
        hostDevices.Count.ShouldBe(1);
        DeviceMapping mapping = hostDevices[0];
        mapping.ShouldSatisfyAllConditions(
            () => mapping.PathOnHost.ShouldBe("/dev/fuse"),
            () => mapping.PathInContainer.ShouldBe("/dev/fuse"),
            () => mapping.CgroupPermissions.ShouldBe("rwm"));
    }

    [Fact]
    public async Task WhenSpecHasEmptyCollections_DoesNotSetSecurityOptOrDevices()
    {
        // Arrange
        FakeDockerContainerRuntime runtime = new();
        DockerWorkerOrchestrator sut = BuildSut(runtime);

        // Act
        await sut.StartAsync(MinimalSpec(), CancellationToken.None);

        // Assert
        CreateContainerParameters captured = runtime.LastCreateAndStartParameters.ShouldNotBeNull();
        captured.HostConfig.SecurityOpt.ShouldBeNull();
        captured.HostConfig.Devices.ShouldBeNull();
    }

    [Fact]
    public async Task WhenSpecHasVolumeMounts_SetsHostConfigMountsWithVolumeType()
    {
        // Arrange
        FakeDockerContainerRuntime runtime = new();
        IReadOnlyList<VolumeMount> volumeMounts =
        [
            new VolumeMount("foundry-claude-credentials", "/home/node/.claude", ReadOnly: false),
        ];
        DockerWorkerOrchestrator sut = BuildSut(runtime);

        // Act
        await sut.StartAsync(MinimalSpec(volumeMounts: volumeMounts), CancellationToken.None);

        // Assert
        CreateContainerParameters captured = runtime.LastCreateAndStartParameters.ShouldNotBeNull();
        IList<Docker.DotNet.Models.Mount> mounts = captured.HostConfig.Mounts.ShouldNotBeNull();
        mounts.Count.ShouldBe(1);
        Docker.DotNet.Models.Mount mount = mounts[0];
        mount.ShouldSatisfyAllConditions(
            () => mount.Type.ShouldBe("volume"),
            () => mount.Source.ShouldBe("foundry-claude-credentials"),
            () => mount.Target.ShouldBe("/home/node/.claude"),
            () => mount.ReadOnly.ShouldBeFalse());
    }

    [Fact]
    public async Task WhenSpecHasNoVolumeMounts_HostConfigMountsIsNull()
    {
        // Arrange
        FakeDockerContainerRuntime runtime = new();
        DockerWorkerOrchestrator sut = BuildSut(runtime);

        // Act
        await sut.StartAsync(MinimalSpec(), CancellationToken.None);

        // Assert
        CreateContainerParameters captured = runtime.LastCreateAndStartParameters.ShouldNotBeNull();
        captured.HostConfig.Mounts.ShouldBeNull();
    }

    [Fact]
    public async Task WhenDockerApiExceptionContainsCredential_ErrorMessageIsRedacted()
    {
        // Arrange
        string rawMessage = "pull access denied for https://user:s3cr3t@registry.example.com/image";
        FakeDockerContainerRuntime runtime = new FakeDockerContainerRuntime()
            .WithCreateAndStartThrowing(new DockerApiException(
                HttpStatusCode.InternalServerError,
                rawMessage));
        DockerWorkerOrchestrator sut = BuildSut(runtime);

        // Act
        Result<ContainerId> result = await sut.StartAsync(MinimalSpec(), CancellationToken.None);

        // Assert
        Result<ContainerId>.Failure failure = result.ShouldBeOfType<Result<ContainerId>.Failure>();
        failure.Error.Message.ShouldContain("https://***@");
        failure.Error.Message.ShouldNotContain("s3cr3t");
    }

    [Fact]
    public async Task WhenDockerApiExceptionContainsKnownToken_ErrorMessageIsRedacted()
    {
        // Arrange
        string rawMessage = "authentication failed: ghp_abc123TokenValue";
        FakeDockerContainerRuntime runtime = new FakeDockerContainerRuntime()
            .WithCreateAndStartThrowing(new DockerApiException(
                HttpStatusCode.Unauthorized,
                rawMessage));
        DockerWorkerOrchestrator sut = BuildSut(runtime);

        // Act
        Result<ContainerId> result = await sut.StartAsync(MinimalSpec(), CancellationToken.None);

        // Assert
        Result<ContainerId>.Failure failure = result.ShouldBeOfType<Result<ContainerId>.Failure>();
        failure.Error.Message.ShouldContain("***");
        failure.Error.Message.ShouldNotContain("ghp_abc123TokenValue");
    }

    [Fact]
    public async Task WhenDockerApiExceptionMessageExceedsMaxLength_RedactionAppliedBeforeTruncation()
    {
        // Arrange
        // DockerApiException.Message prepends a fixed prefix before the raw body:
        // "Docker API responded with status code=InternalServerError, response=<body>"
        // Measure the prefix length at runtime by constructing a throwaway exception with
        // an empty body — this stays resilient if Docker.DotNet ever changes the format.
        // Position the URL credential so that https:// starts at offset 480 in the full
        // message and the '@' lands at offset 507 (past the 500-char truncation boundary).
        // Truncating first cuts the URL to "https://user:s3cr3t" (no '@'), so the
        // https://[^@/\s]+@ pattern cannot match and the password leaks.
        // Redacting first catches the full URL and replaces "https://user:s3cr3t@" with
        // "https://***@" before truncation, so the password never appears.
        int fullMessagePrefixLength = new DockerApiException(HttpStatusCode.InternalServerError, "").Message.Length;
        const int UrlStartPositionInFullMessage = 480;
        int paddingLength = UrlStartPositionInFullMessage - fullMessagePrefixLength;
        string padding = new('x', paddingLength);
        string rawMessage = padding + "https://user:s3cr3tpassword@registry.example.com/image";
        FakeDockerContainerRuntime runtime = new FakeDockerContainerRuntime()
            .WithCreateAndStartThrowing(new DockerApiException(
                HttpStatusCode.InternalServerError,
                rawMessage));
        DockerWorkerOrchestrator sut = BuildSut(runtime);

        // Act
        Result<ContainerId> result = await sut.StartAsync(MinimalSpec(), CancellationToken.None);

        // Assert
        Result<ContainerId>.Failure failure = result.ShouldBeOfType<Result<ContainerId>.Failure>();
        failure.Error.Message.ShouldNotContain("s3cr3t");
    }
}
