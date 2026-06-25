using System.Net;

using Docker.DotNet;
using Docker.DotNet.Models;

using Foundry.Modules.Workers.Domain;
using Foundry.Modules.Workers.Features;
using Foundry.Modules.Workers.Infrastructure;
using Foundry.Shared;

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
        IReadOnlyList<string>? devices = null) =>
        new(
            Image: image,
            EnvironmentVariables: new Dictionary<string, string>(),
            BindMounts: bindMounts ?? [],
            Labels: new Dictionary<string, string>(),
            Command: [])
        {
            SecurityOptions = securityOptions ?? [],
            Devices = devices ?? [],
        };

    [Fact]
    public async Task WhenStarted_SetsMemoryLimitFromOptions()
    {
        // Arrange
        SpyContainerOperations spy = new();
        WorkerOptions options = DefaultOptions();
        DockerWorkerOrchestrator sut = new(spy, Options.Create(options));

        // Act
        await sut.StartAsync(MinimalSpec(), CancellationToken.None);

        // Assert
        CreateContainerParameters captured = spy.LastCreateParameters.ShouldNotBeNull();
        captured.HostConfig.Memory.ShouldBe(options.MemoryLimitMb * BytesPerMegabyte);
    }

    [Fact]
    public async Task WhenStarted_SetsNanoCpusFromOptions()
    {
        // Arrange
        SpyContainerOperations spy = new();
        WorkerOptions options = DefaultOptions();
        DockerWorkerOrchestrator sut = new(spy, Options.Create(options));

        // Act
        await sut.StartAsync(MinimalSpec(), CancellationToken.None);

        // Assert
        CreateContainerParameters captured = spy.LastCreateParameters.ShouldNotBeNull();
        captured.HostConfig.NanoCPUs.ShouldBe((long)(options.CpuLimit * NanoCpusPerCpu));
    }

    [Fact]
    public async Task WhenStarted_SetsPidsLimitFromOptions()
    {
        // Arrange
        SpyContainerOperations spy = new();
        WorkerOptions options = DefaultOptions();
        DockerWorkerOrchestrator sut = new(spy, Options.Create(options));

        // Act
        await sut.StartAsync(MinimalSpec(), CancellationToken.None);

        // Assert
        CreateContainerParameters captured = spy.LastCreateParameters.ShouldNotBeNull();
        captured.HostConfig.PidsLimit.ShouldBe(options.PidsLimit);
    }

    [Fact]
    public async Task WhenStarted_SetsBindsFromSpec()
    {
        // Arrange
        SpyContainerOperations spy = new();
        IReadOnlyList<BindMount> bindMounts =
        [
            new BindMount("/host/data", "/container/data", ReadOnly: false),
            new BindMount("/host/config", "/container/config", ReadOnly: true),
        ];
        DockerWorkerOrchestrator sut = new(spy, Options.Create(DefaultOptions()));

        // Act
        await sut.StartAsync(MinimalSpec(bindMounts: bindMounts), CancellationToken.None);

        // Assert
        CreateContainerParameters captured = spy.LastCreateParameters.ShouldNotBeNull();
        captured.HostConfig.Binds.ShouldBe(
            ["/host/data:/container/data", "/host/config:/container/config:ro"],
            ignoreOrder: false);
    }

    [Fact]
    public async Task WhenStarted_ReturnsContainerIdOnSuccess()
    {
        // Arrange
        SpyContainerOperations spy = new("created-container-abc");
        DockerWorkerOrchestrator sut = new(spy, Options.Create(DefaultOptions()));

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
        SpyContainerOperations spy = new();
        IReadOnlyList<string> securityOptions = ["seccomp=unconfined", "apparmor=unconfined"];
        DockerWorkerOrchestrator sut = new(spy, Options.Create(DefaultOptions()));

        // Act
        await sut.StartAsync(MinimalSpec(securityOptions: securityOptions), CancellationToken.None);

        // Assert
        CreateContainerParameters captured = spy.LastCreateParameters.ShouldNotBeNull();
        captured.HostConfig.SecurityOpt.ShouldBe(
            ["seccomp=unconfined", "apparmor=unconfined"],
            ignoreOrder: false);
    }

    [Fact]
    public async Task WhenSpecHasDevices_SetsHostConfigDevicesWithCorrectMapping()
    {
        // Arrange
        SpyContainerOperations spy = new();
        IReadOnlyList<string> devices = ["/dev/fuse"];
        DockerWorkerOrchestrator sut = new(spy, Options.Create(DefaultOptions()));

        // Act
        await sut.StartAsync(MinimalSpec(devices: devices), CancellationToken.None);

        // Assert
        CreateContainerParameters captured = spy.LastCreateParameters.ShouldNotBeNull();
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
        SpyContainerOperations spy = new();
        DockerWorkerOrchestrator sut = new(spy, Options.Create(DefaultOptions()));

        // Act
        await sut.StartAsync(MinimalSpec(), CancellationToken.None);

        // Assert
        CreateContainerParameters captured = spy.LastCreateParameters.ShouldNotBeNull();
        captured.HostConfig.SecurityOpt.ShouldBeNull();
        captured.HostConfig.Devices.ShouldBeNull();
    }

    [Fact]
    public async Task WhenDockerApiExceptionContainsCredential_ErrorMessageIsRedacted()
    {
        // Arrange
        string rawMessage = "pull access denied for https://user:s3cr3t@registry.example.com/image";
        ThrowingContainerOperations throwing = new(new DockerApiException(
            HttpStatusCode.InternalServerError,
            rawMessage));
        DockerWorkerOrchestrator sut = new(throwing, Options.Create(DefaultOptions()));

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
        ThrowingContainerOperations throwing = new(new DockerApiException(
            HttpStatusCode.Unauthorized,
            rawMessage));
        DockerWorkerOrchestrator sut = new(throwing, Options.Create(DefaultOptions()));

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
        ThrowingContainerOperations throwing = new(new DockerApiException(
            HttpStatusCode.InternalServerError,
            rawMessage));
        DockerWorkerOrchestrator sut = new(throwing, Options.Create(DefaultOptions()));

        // Act
        Result<ContainerId> result = await sut.StartAsync(MinimalSpec(), CancellationToken.None);

        // Assert
        Result<ContainerId>.Failure failure = result.ShouldBeOfType<Result<ContainerId>.Failure>();
        failure.Error.Message.ShouldNotContain("s3cr3t");
    }

    private sealed class ThrowingContainerOperations(DockerApiException exception) : IContainerOperations
    {
        public Task<CreateContainerResponse> CreateContainerAsync(
            CreateContainerParameters parameters,
            CancellationToken cancellationToken)
            => throw exception;

        public Task<bool> StartContainerAsync(
            string id,
            ContainerStartParameters parameters,
            CancellationToken cancellationToken)
            => throw exception;

        public Task<bool> StopContainerAsync(
            string id,
            ContainerStopParameters parameters,
            CancellationToken cancellationToken)
            => Task.FromResult(true);

        public Task RemoveContainerAsync(
            string id,
            ContainerRemoveParameters parameters,
            CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<ContainerInspectResponse> InspectContainerAsync(
            string id,
            CancellationToken cancellationToken)
            => Task.FromResult(new ContainerInspectResponse());

        public Task<IList<ContainerListResponse>> ListContainersAsync(
            ContainersListParameters parameters,
            CancellationToken cancellationToken)
            => Task.FromResult<IList<ContainerListResponse>>([]);

        public Task<MultiplexedStream> GetContainerLogsAsync(
            string id,
            bool tty,
            ContainerLogsParameters parameters,
            CancellationToken cancellationToken)
            => Task.FromResult(new MultiplexedStream(Stream.Null, false));

        public Task<MultiplexedStream> AttachContainerAsync(
            string id,
            bool tty,
            ContainerAttachParameters parameters,
            CancellationToken cancellationToken)
            => Task.FromResult(new MultiplexedStream(Stream.Null, false));

        public Task<Stream> ExportContainerAsync(string id, CancellationToken cancellationToken)
            => Task.FromResult<Stream>(Stream.Null);

        public Task ExtractArchiveToContainerAsync(
            string id,
            ContainerPathStatParameters parameters,
            Stream stream,
            CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<GetArchiveFromContainerResponse> GetArchiveFromContainerAsync(
            string id,
            GetArchiveFromContainerParameters parameters,
            bool statOnly,
            CancellationToken cancellationToken)
            => Task.FromResult(new GetArchiveFromContainerResponse());

        public Task<Stream> GetContainerLogsAsync(
            string id,
            ContainerLogsParameters parameters,
            CancellationToken cancellationToken)
            => Task.FromResult<Stream>(Stream.Null);

        public Task GetContainerLogsAsync(
            string id,
            ContainerLogsParameters parameters,
            CancellationToken cancellationToken,
            IProgress<string> progress)
            => Task.CompletedTask;

        public Task<Stream> GetContainerStatsAsync(
            string id,
            ContainerStatsParameters parameters,
            CancellationToken cancellationToken)
            => Task.FromResult<Stream>(Stream.Null);

        public Task GetContainerStatsAsync(
            string id,
            ContainerStatsParameters parameters,
            IProgress<ContainerStatsResponse> progress,
            CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<IList<ContainerFileSystemChangeResponse>> InspectChangesAsync(
            string id,
            CancellationToken cancellationToken)
            => Task.FromResult<IList<ContainerFileSystemChangeResponse>>([]);

        public Task KillContainerAsync(
            string id,
            ContainerKillParameters parameters,
            CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<ContainerProcessesResponse> ListProcessesAsync(
            string id,
            ContainerListProcessesParameters parameters,
            CancellationToken cancellationToken)
            => Task.FromResult(new ContainerProcessesResponse());

        public Task PauseContainerAsync(string id, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<ContainersPruneResponse> PruneContainersAsync(
            ContainersPruneParameters parameters,
            CancellationToken cancellationToken)
            => Task.FromResult(new ContainersPruneResponse());

        public Task RenameContainerAsync(
            string id,
            ContainerRenameParameters parameters,
            CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task ResizeContainerTtyAsync(
            string id,
            ContainerResizeParameters parameters,
            CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task RestartContainerAsync(
            string id,
            ContainerRestartParameters parameters,
            CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task UnpauseContainerAsync(string id, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<ContainerUpdateResponse> UpdateContainerAsync(
            string id,
            ContainerUpdateParameters parameters,
            CancellationToken cancellationToken)
            => Task.FromResult(new ContainerUpdateResponse());

        public Task<ContainerWaitResponse> WaitContainerAsync(
            string id,
            CancellationToken cancellationToken)
            => Task.FromResult(new ContainerWaitResponse());
    }

    private sealed class SpyContainerOperations(string containerId = "spy-container-id") : IContainerOperations
    {
        public CreateContainerParameters? LastCreateParameters { get; private set; }

        public Task<CreateContainerResponse> CreateContainerAsync(
            CreateContainerParameters parameters,
            CancellationToken cancellationToken)
        {
            LastCreateParameters = parameters;
            return Task.FromResult(new CreateContainerResponse { ID = containerId });
        }

        public Task<bool> StartContainerAsync(
            string id,
            ContainerStartParameters parameters,
            CancellationToken cancellationToken)
            => Task.FromResult(true);

        public Task<bool> StopContainerAsync(
            string id,
            ContainerStopParameters parameters,
            CancellationToken cancellationToken)
            => Task.FromResult(true);

        public Task RemoveContainerAsync(
            string id,
            ContainerRemoveParameters parameters,
            CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<ContainerInspectResponse> InspectContainerAsync(
            string id,
            CancellationToken cancellationToken)
            => Task.FromResult(new ContainerInspectResponse());

        public Task<IList<ContainerListResponse>> ListContainersAsync(
            ContainersListParameters parameters,
            CancellationToken cancellationToken)
            => Task.FromResult<IList<ContainerListResponse>>([]);

        public Task<MultiplexedStream> GetContainerLogsAsync(
            string id,
            bool tty,
            ContainerLogsParameters parameters,
            CancellationToken cancellationToken)
            => Task.FromResult(new MultiplexedStream(Stream.Null, false));

        public Task<MultiplexedStream> AttachContainerAsync(
            string id,
            bool tty,
            ContainerAttachParameters parameters,
            CancellationToken cancellationToken)
            => Task.FromResult(new MultiplexedStream(Stream.Null, false));

        public Task<Stream> ExportContainerAsync(string id, CancellationToken cancellationToken)
            => Task.FromResult<Stream>(Stream.Null);

        public Task ExtractArchiveToContainerAsync(
            string id,
            ContainerPathStatParameters parameters,
            Stream stream,
            CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<GetArchiveFromContainerResponse> GetArchiveFromContainerAsync(
            string id,
            GetArchiveFromContainerParameters parameters,
            bool statOnly,
            CancellationToken cancellationToken)
            => Task.FromResult(new GetArchiveFromContainerResponse());

        public Task<Stream> GetContainerLogsAsync(
            string id,
            ContainerLogsParameters parameters,
            CancellationToken cancellationToken)
            => Task.FromResult<Stream>(Stream.Null);

        public Task GetContainerLogsAsync(
            string id,
            ContainerLogsParameters parameters,
            CancellationToken cancellationToken,
            IProgress<string> progress)
            => Task.CompletedTask;

        public Task<Stream> GetContainerStatsAsync(
            string id,
            ContainerStatsParameters parameters,
            CancellationToken cancellationToken)
            => Task.FromResult<Stream>(Stream.Null);

        public Task GetContainerStatsAsync(
            string id,
            ContainerStatsParameters parameters,
            IProgress<ContainerStatsResponse> progress,
            CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<IList<ContainerFileSystemChangeResponse>> InspectChangesAsync(
            string id,
            CancellationToken cancellationToken)
            => Task.FromResult<IList<ContainerFileSystemChangeResponse>>([]);

        public Task KillContainerAsync(
            string id,
            ContainerKillParameters parameters,
            CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<ContainerProcessesResponse> ListProcessesAsync(
            string id,
            ContainerListProcessesParameters parameters,
            CancellationToken cancellationToken)
            => Task.FromResult(new ContainerProcessesResponse());

        public Task PauseContainerAsync(string id, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<ContainersPruneResponse> PruneContainersAsync(
            ContainersPruneParameters parameters,
            CancellationToken cancellationToken)
            => Task.FromResult(new ContainersPruneResponse());

        public Task RenameContainerAsync(
            string id,
            ContainerRenameParameters parameters,
            CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task ResizeContainerTtyAsync(
            string id,
            ContainerResizeParameters parameters,
            CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task RestartContainerAsync(
            string id,
            ContainerRestartParameters parameters,
            CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task UnpauseContainerAsync(string id, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<ContainerUpdateResponse> UpdateContainerAsync(
            string id,
            ContainerUpdateParameters parameters,
            CancellationToken cancellationToken)
            => Task.FromResult(new ContainerUpdateResponse());

        public Task<ContainerWaitResponse> WaitContainerAsync(
            string id,
            CancellationToken cancellationToken)
            => Task.FromResult(new ContainerWaitResponse());
    }
}
