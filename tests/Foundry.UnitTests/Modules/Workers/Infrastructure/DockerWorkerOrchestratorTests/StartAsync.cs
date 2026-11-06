using System.Net;

using Docker.DotNet;
using Docker.DotNet.Models;

using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Orchestration;
using Foundry.Modules.Workers.Features.ContainerSpec;
using Foundry.Modules.Workers.Infrastructure;
using Foundry.Shared;
using Foundry.UnitTests.Fakes.Workers;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Infrastructure.DockerWorkerOrchestratorTests;

public sealed class StartAsync
{
    private const long BytesPerMegabyte = 1024L * 1024L;
    private const long NanoCpusPerCpu = 1_000_000_000L;
    private const int DefaultMemoryLimitMb = 512;
    private const double DefaultCpuLimit = 1.5;
    private const int DefaultPidsLimit = 256;

    private static WorkerContainerSpec MinimalSpec(
        string image = "test-image:latest",
        IReadOnlyList<string>? securityOptions = null,
        IReadOnlyList<string>? devices = null,
        IReadOnlyList<VolumeMount>? volumeMounts = null,
        IReadOnlyList<string>? capabilities = null,
        IReadOnlyDictionary<string, string>? labels = null,
        bool unmaskSystemPaths = false,
        IReadOnlyList<string>? droppedCapabilities = null,
        bool readOnlyRootFilesystem = false,
        IReadOnlyDictionary<string, string>? tmpfsMounts = null,
        IReadOnlyList<string>? scratchVolumePaths = null,
        int memoryLimitMb = DefaultMemoryLimitMb,
        double cpuLimit = DefaultCpuLimit,
        int pidsLimit = DefaultPidsLimit) =>
        new(
            Image: image,
            EnvironmentVariables: new Dictionary<string, string>(),
            Labels: labels ?? new Dictionary<string, string>(),
            Command: [])
        {
            MemoryLimitMb = memoryLimitMb,
            CpuLimit = cpuLimit,
            PidsLimit = pidsLimit,
            SecurityOptions = securityOptions ?? [],
            Devices = devices ?? [],
            VolumeMounts = volumeMounts ?? [],
            Capabilities = capabilities ?? [],
            UnmaskSystemPaths = unmaskSystemPaths,
            DroppedCapabilities = droppedCapabilities ?? [],
            ReadOnlyRootFilesystem = readOnlyRootFilesystem,
            TmpfsMounts = tmpfsMounts ?? new Dictionary<string, string>(),
            ScratchVolumePaths = scratchVolumePaths ?? [],
        };

    private static DockerWorkerOrchestrator BuildSut(FakeDockerContainerRuntime runtime) => new(runtime);

    [Fact]
    public async Task WhenStarted_SetsMemoryLimitFromSpec()
    {
        // Arrange
        FakeDockerContainerRuntime runtime = new();
        DockerWorkerOrchestrator sut = BuildSut(runtime);

        // Act
        await sut.StartAsync(MinimalSpec(memoryLimitMb: 768), CancellationToken.None);

        // Assert
        CreateContainerParameters captured = runtime.LastCreateAndStartParameters.ShouldNotBeNull();
        captured.HostConfig.Memory.ShouldBe(768 * BytesPerMegabyte);
    }

    [Fact]
    public async Task WhenStarted_SetsNanoCpusFromSpec()
    {
        // Arrange
        FakeDockerContainerRuntime runtime = new();
        DockerWorkerOrchestrator sut = BuildSut(runtime);

        // Act
        await sut.StartAsync(MinimalSpec(cpuLimit: 0.5), CancellationToken.None);

        // Assert
        CreateContainerParameters captured = runtime.LastCreateAndStartParameters.ShouldNotBeNull();
        captured.HostConfig.NanoCPUs.ShouldBe((long)(0.5 * NanoCpusPerCpu));
    }

    [Fact]
    public async Task WhenStarted_SetsPidsLimitFromSpec()
    {
        // Arrange
        FakeDockerContainerRuntime runtime = new();
        DockerWorkerOrchestrator sut = BuildSut(runtime);

        // Act
        await sut.StartAsync(MinimalSpec(pidsLimit: 128), CancellationToken.None);

        // Assert
        CreateContainerParameters captured = runtime.LastCreateAndStartParameters.ShouldNotBeNull();
        captured.HostConfig.PidsLimit.ShouldBe(128);
    }

    [Fact]
    public async Task WhenStarted_SetsNoHostBinds()
    {
        // Arrange
        FakeDockerContainerRuntime runtime = new();
        DockerWorkerOrchestrator sut = BuildSut(runtime);

        // Act
        await sut.StartAsync(MinimalSpec(), CancellationToken.None);

        // Assert
        CreateContainerParameters captured = runtime.LastCreateAndStartParameters.ShouldNotBeNull();
        captured.HostConfig.Binds.ShouldBeNull();
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
        captured.HostConfig.CapAdd.ShouldBeNull();
    }

    [Fact]
    public async Task WhenSpecUnmasksSystemPaths_MaskedAndReadonlyPathsAreEmptyLists()
    {
        // Arrange
        FakeDockerContainerRuntime runtime = new();
        DockerWorkerOrchestrator sut = BuildSut(runtime);

        // Act
        await sut.StartAsync(MinimalSpec(unmaskSystemPaths: true), CancellationToken.None);

        // Assert
        HostConfig hostConfig = runtime.LastCreateAndStartParameters.ShouldNotBeNull().HostConfig;
        hostConfig.ShouldSatisfyAllConditions(
            () => hostConfig.MaskedPaths.ShouldNotBeNull().ShouldBeEmpty(),
            () => hostConfig.ReadonlyPaths.ShouldNotBeNull().ShouldBeEmpty());
    }

    [Fact]
    public async Task WhenSpecKeepsDefaultMasking_MaskedAndReadonlyPathsAreNull()
    {
        // Arrange
        FakeDockerContainerRuntime runtime = new();
        DockerWorkerOrchestrator sut = BuildSut(runtime);

        // Act
        await sut.StartAsync(MinimalSpec(), CancellationToken.None);

        // Assert
        HostConfig hostConfig = runtime.LastCreateAndStartParameters.ShouldNotBeNull().HostConfig;
        hostConfig.ShouldSatisfyAllConditions(
            () => hostConfig.MaskedPaths.ShouldBeNull(),
            () => hostConfig.ReadonlyPaths.ShouldBeNull());
    }

    [Fact]
    public async Task WhenSpecHasCapabilities_SetsHostConfigCapAdd()
    {
        // Arrange
        FakeDockerContainerRuntime runtime = new();
        IReadOnlyList<string> capabilities = ["SYS_ADMIN"];
        DockerWorkerOrchestrator sut = BuildSut(runtime);

        // Act
        await sut.StartAsync(MinimalSpec(capabilities: capabilities), CancellationToken.None);

        // Assert
        CreateContainerParameters captured = runtime.LastCreateAndStartParameters.ShouldNotBeNull();
        captured.HostConfig.CapAdd.ShouldBe(["SYS_ADMIN"], ignoreOrder: false);
    }

    [Fact]
    public async Task WhenSpecDropsCapabilities_SetsHostConfigCapDrop()
    {
        // Arrange
        FakeDockerContainerRuntime runtime = new();
        DockerWorkerOrchestrator sut = BuildSut(runtime);

        // Act
        await sut.StartAsync(MinimalSpec(droppedCapabilities: ["ALL"]), CancellationToken.None);

        // Assert
        CreateContainerParameters captured = runtime.LastCreateAndStartParameters.ShouldNotBeNull();
        captured.HostConfig.CapDrop.ShouldBe(["ALL"]);
    }

    [Fact]
    public async Task WhenSpecDropsNoCapabilities_HostConfigCapDropIsNull()
    {
        // Arrange
        FakeDockerContainerRuntime runtime = new();
        DockerWorkerOrchestrator sut = BuildSut(runtime);

        // Act
        await sut.StartAsync(MinimalSpec(), CancellationToken.None);

        // Assert
        CreateContainerParameters captured = runtime.LastCreateAndStartParameters.ShouldNotBeNull();
        captured.HostConfig.CapDrop.ShouldBeNull();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task WhenStarted_SetsReadonlyRootfsFromSpec(bool readOnly)
    {
        // Arrange
        FakeDockerContainerRuntime runtime = new();
        DockerWorkerOrchestrator sut = BuildSut(runtime);

        // Act
        await sut.StartAsync(MinimalSpec(readOnlyRootFilesystem: readOnly), CancellationToken.None);

        // Assert
        CreateContainerParameters captured = runtime.LastCreateAndStartParameters.ShouldNotBeNull();
        captured.HostConfig.ReadonlyRootfs.ShouldBe(readOnly);
    }

    [Fact]
    public async Task WhenSpecHasTmpfsMounts_SetsHostConfigTmpfs()
    {
        // Arrange
        FakeDockerContainerRuntime runtime = new();
        Dictionary<string, string> tmpfs = new()
        {
            ["/tmp"] = "rw,nosuid,mode=1777",
            ["/etc/claude-code"] = "rw,noexec,mode=0755",
        };
        DockerWorkerOrchestrator sut = BuildSut(runtime);

        // Act
        await sut.StartAsync(MinimalSpec(tmpfsMounts: tmpfs), CancellationToken.None);

        // Assert
        CreateContainerParameters captured = runtime.LastCreateAndStartParameters.ShouldNotBeNull();
        captured.HostConfig.Tmpfs.ShouldNotBeNull().ShouldBe(tmpfs, ignoreOrder: true);
    }

    [Fact]
    public async Task WhenSpecHasNoTmpfsMounts_HostConfigTmpfsIsNull()
    {
        // Arrange
        FakeDockerContainerRuntime runtime = new();
        DockerWorkerOrchestrator sut = BuildSut(runtime);

        // Act
        await sut.StartAsync(MinimalSpec(), CancellationToken.None);

        // Assert
        CreateContainerParameters captured = runtime.LastCreateAndStartParameters.ShouldNotBeNull();
        captured.HostConfig.Tmpfs.ShouldBeNull();
    }

    [Fact]
    public async Task WhenSpecHasScratchVolumePaths_MountsAnonymousVolumesBeforeNamedOnes()
    {
        // Arrange
        FakeDockerContainerRuntime runtime = new();
        DockerWorkerOrchestrator sut = BuildSut(runtime);

        // Act
        await sut.StartAsync(
            MinimalSpec(
                scratchVolumePaths: ["/workspace", "/home/node"],
                volumeMounts: [new VolumeMount("foundry-claude-credentials", "/home/node/.claude")]),
            CancellationToken.None);

        // Assert
        CreateContainerParameters captured = runtime.LastCreateAndStartParameters.ShouldNotBeNull();
        IList<Docker.DotNet.Models.Mount> mounts = captured.HostConfig.Mounts.ShouldNotBeNull();
        mounts.Select(m => m.Target).ShouldBe(["/workspace", "/home/node", "/home/node/.claude"], ignoreOrder: false);
        mounts.ShouldSatisfyAllConditions(
            () => mounts.ShouldAllBe(m => m.Type == "volume"),
            () => mounts.Take(2).ShouldAllBe(m => string.IsNullOrEmpty(m.Source) && !m.ReadOnly),
            () => mounts[2].Source.ShouldBe("foundry-claude-credentials"));
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
    public async Task WhenSpecHasDataRootVolumeMount_CreatesVolumeWithManagedAndWorkerRunIdLabels()
    {
        // Arrange
        WorkerRunId runId = WorkerRunId.New();
        string dataRootVolumeName = WorkerVolumeNames.DataRootVolumeName(runId);
        FakeDockerContainerRuntime runtime = new();
        IReadOnlyList<VolumeMount> volumeMounts =
        [
            new VolumeMount(dataRootVolumeName, WorkerVolumeNames.DataRootContainerPath),
        ];
        IReadOnlyDictionary<string, string> labels = new Dictionary<string, string>
        {
            ["foundry.worker-run-id"] = runId.Value.ToString(),
        };
        DockerWorkerOrchestrator sut = BuildSut(runtime);

        // Act
        await sut.StartAsync(MinimalSpec(volumeMounts: volumeMounts, labels: labels), CancellationToken.None);

        // Assert
        VolumesCreateParameters created = runtime.LastCreateVolumeParameters.ShouldNotBeNull();
        created.ShouldSatisfyAllConditions(
            () => created.Name.ShouldBe(dataRootVolumeName),
            () => created.Labels["foundry.managed"].ShouldBe("true"),
            () => created.Labels["foundry.worker-run-id"].ShouldBe(runId.Value.ToString()));
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
