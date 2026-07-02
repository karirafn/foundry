using Docker.DotNet;
using Docker.DotNet.Models;

using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain;
using Foundry.Modules.Workers.Features;
using Foundry.Modules.Workers.Features.Login;
using Foundry.Modules.Workers.Infrastructure;
using Foundry.Shared;
using Foundry.UnitTests.Fakes.Workers;

using Microsoft.Extensions.Options;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Infrastructure.DockerWorkerOrchestratorTests;

public sealed class StartLoginContainerAsync
{
    private static WorkerOptions DefaultOptions() => new()
    {
        Image = "test-image:latest",
        MemoryLimitMb = 512,
        CpuLimit = 1.5,
        PidsLimit = 256,
    };

    private static DockerWorkerOrchestrator BuildSut(
        SpyContainerOperations containerOps,
        FakeExecOperations? execOps = null) =>
        new(containerOps, new NullVolumeOperations(), execOps ?? new FakeExecOperations(), Options.Create(DefaultOptions()));

    [Fact]
    public async Task WhenStarted_UsesLoginImageName()
    {
        // Arrange
        SpyContainerOperations spy = new();
        DockerWorkerOrchestrator sut = BuildSut(spy);

        // Act
        await sut.StartLoginContainerAsync(new LoginContainerSpec(TimeoutSeconds: 600), CancellationToken.None);

        // Assert
        CreateContainerParameters captured = spy.LastCreateParameters.ShouldNotBeNull();
        captured.Image.ShouldBe(WorkerImageNames.LoginImageName);
    }

    [Fact]
    public async Task WhenStarted_SetsTtyFalse()
    {
        // Arrange
        SpyContainerOperations spy = new();
        DockerWorkerOrchestrator sut = BuildSut(spy);

        // Act
        await sut.StartLoginContainerAsync(new LoginContainerSpec(TimeoutSeconds: 600), CancellationToken.None);

        // Assert
        CreateContainerParameters captured = spy.LastCreateParameters.ShouldNotBeNull();
        captured.Tty.ShouldBeFalse();
    }

    [Fact]
    public async Task WhenStarted_SetsWorkingDirToHomeNode()
    {
        // Arrange
        SpyContainerOperations spy = new();
        DockerWorkerOrchestrator sut = BuildSut(spy);

        // Act
        await sut.StartLoginContainerAsync(new LoginContainerSpec(TimeoutSeconds: 600), CancellationToken.None);

        // Assert
        CreateContainerParameters captured = spy.LastCreateParameters.ShouldNotBeNull();
        captured.WorkingDir.ShouldBe(OnboardingSeed.DefaultWorkDir);
    }

    [Fact]
    public async Task WhenStarted_SetsClaudeConfigDirEnv()
    {
        // Arrange
        SpyContainerOperations spy = new();
        DockerWorkerOrchestrator sut = BuildSut(spy);

        // Act
        await sut.StartLoginContainerAsync(new LoginContainerSpec(TimeoutSeconds: 600), CancellationToken.None);

        // Assert
        CreateContainerParameters captured = spy.LastCreateParameters.ShouldNotBeNull();
        string expectedEnv = $"{WorkerVolumeNames.ClaudeConfigDirEnvVar}={WorkerVolumeNames.ClaudeConfigContainerPath}";
        captured.Env.ShouldContain(expectedEnv);
    }

    [Fact]
    public async Task WhenStarted_MountsCredentialVolume()
    {
        // Arrange
        SpyContainerOperations spy = new();
        DockerWorkerOrchestrator sut = BuildSut(spy);

        // Act
        await sut.StartLoginContainerAsync(new LoginContainerSpec(TimeoutSeconds: 600), CancellationToken.None);

        // Assert
        CreateContainerParameters captured = spy.LastCreateParameters.ShouldNotBeNull();
        IList<Mount> mounts = captured.HostConfig.Mounts.ShouldNotBeNull();
        mounts.ShouldContain(m =>
            m.Type == "volume"
            && m.Source == WorkerVolumeNames.CredentialVolumeName
            && m.Target == WorkerVolumeNames.ClaudeConfigContainerPath
            && !m.ReadOnly);
    }

    [Fact]
    public async Task WhenStarted_SetsLoginLabel()
    {
        // Arrange
        SpyContainerOperations spy = new();
        DockerWorkerOrchestrator sut = BuildSut(spy);

        // Act
        await sut.StartLoginContainerAsync(new LoginContainerSpec(TimeoutSeconds: 600), CancellationToken.None);

        // Assert
        CreateContainerParameters captured = spy.LastCreateParameters.ShouldNotBeNull();
        captured.Labels.ShouldContainKey("foundry.login");
        captured.Labels["foundry.login"].ShouldBe("true");
    }

    [Fact]
    public async Task WhenStarted_SetsAttachStdoutAndStderr()
    {
        // Arrange
        SpyContainerOperations spy = new();
        DockerWorkerOrchestrator sut = BuildSut(spy);

        // Act
        await sut.StartLoginContainerAsync(new LoginContainerSpec(TimeoutSeconds: 600), CancellationToken.None);

        // Assert
        CreateContainerParameters captured = spy.LastCreateParameters.ShouldNotBeNull();
        captured.ShouldSatisfyAllConditions(
            () => captured.AttachStdout.ShouldBeTrue(),
            () => captured.AttachStderr.ShouldBeTrue());
    }

    [Fact]
    public async Task WhenStarted_CmdContainsFifoBootstrap()
    {
        // Arrange
        SpyContainerOperations spy = new();
        DockerWorkerOrchestrator sut = BuildSut(spy);

        // Act
        await sut.StartLoginContainerAsync(new LoginContainerSpec(TimeoutSeconds: 600), CancellationToken.None);

        // Assert
        CreateContainerParameters captured = spy.LastCreateParameters.ShouldNotBeNull();
        string cmdStr = string.Join(" ", captured.Cmd);
        cmdStr.ShouldSatisfyAllConditions(
            () => cmdStr.ShouldContain("mkfifo"),
            () => cmdStr.ShouldContain(LoginExecCommand.FifoPath),
            () => cmdStr.ShouldContain("sleep 600"),
            () => cmdStr.ShouldContain("exec claude auth login --claudeai"),
            () => cmdStr.ShouldContain($"< {LoginExecCommand.FifoPath}"));
    }

    [Fact]
    public async Task WhenStarted_ReturnsContainerIdOnSuccess()
    {
        // Arrange
        SpyContainerOperations spy = new("login-container-xyz");
        DockerWorkerOrchestrator sut = BuildSut(spy);

        // Act
        Result<ContainerId> result = await sut.StartLoginContainerAsync(
            new LoginContainerSpec(TimeoutSeconds: 600),
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<ContainerId>.Success success = result.ShouldBeOfType<Result<ContainerId>.Success>();
        success.Value.Value.ShouldBe("login-container-xyz");
    }

    private sealed class NullVolumeOperations : IVolumeOperations
    {
        public Task<VolumeResponse> CreateAsync(
            VolumesCreateParameters parameters,
            CancellationToken cancellationToken)
            => Task.FromResult(new VolumeResponse { Name = parameters.Name });

        public Task<VolumeResponse> InspectAsync(string name, CancellationToken cancellationToken)
            => Task.FromResult(new VolumeResponse { Name = name });

        public Task<VolumesListResponse> ListAsync(CancellationToken cancellationToken)
            => Task.FromResult(new VolumesListResponse());

        public Task<VolumesListResponse> ListAsync(
            VolumesListParameters parameters,
            CancellationToken cancellationToken)
            => Task.FromResult(new VolumesListResponse());

        public Task<VolumesPruneResponse> PruneAsync(
            VolumesPruneParameters parameters,
            CancellationToken cancellationToken)
            => Task.FromResult(new VolumesPruneResponse());

        public Task RemoveAsync(string name, bool? force, CancellationToken cancellationToken)
            => Task.CompletedTask;
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
