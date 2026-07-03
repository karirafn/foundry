using Docker.DotNet;
using Docker.DotNet.Models;

using Foundry.Modules.Workers.Features;
using Foundry.Modules.Workers.Features.Login;
using Foundry.Modules.Workers.Infrastructure;
using Foundry.UnitTests.Fakes.Workers;

using Microsoft.Extensions.Options;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Infrastructure.DockerWorkerOrchestratorTests;

public sealed class DeliverLoginCodeAsync
{
    private static WorkerOptions DefaultOptions() => new()
    {
        Image = "test-image:latest",
        MemoryLimitMb = 512,
        CpuLimit = 1.5,
        PidsLimit = 256,
    };

    private static DockerWorkerOrchestrator BuildSut(CapturingExecOperations execOps) =>
        new(
            new NullContainerOperations(),
            new NullVolumeOperations(),
            execOps,
            Options.Create(DefaultOptions()));

    [Fact]
    public async Task WhenCodeDelivered_ExecCreatedInCorrectContainer()
    {
        // Arrange
        CapturingExecOperations execOps = new();
        DockerWorkerOrchestrator sut = BuildSut(execOps);

        // Act
        await sut.DeliverLoginCodeAsync("test-container-id", "my-code", CancellationToken.None);

        // Assert
        execOps.LastContainerId.ShouldBe("test-container-id");
    }

    [Fact]
    public async Task WhenCodeDelivered_ExecArgvMatchesForCodeCommand()
    {
        // Arrange
        CapturingExecOperations execOps = new();
        DockerWorkerOrchestrator sut = BuildSut(execOps);

        // Act
        await sut.DeliverLoginCodeAsync("container-id", "auth-code-abc", CancellationToken.None);

        // Assert — argv uses env-var delivery (ForCode), not stdin (ForStdin)
        LoginExecCommand expectedCmd = LoginExecCommand.ForCode("auth-code-abc");
        ContainerExecCreateParameters captured = execOps.LastCreateParameters.ShouldNotBeNull();
        captured.Cmd.ShouldBe(expectedCmd.Argv);
    }

    [Fact]
    public async Task WhenCodeDelivered_CodeIsInEnvVarNotArgv()
    {
        // Arrange
        CapturingExecOperations execOps = new();
        DockerWorkerOrchestrator sut = BuildSut(execOps);

        // Act
        await sut.DeliverLoginCodeAsync("container-id", "secret-code", CancellationToken.None);

        // Assert — code carried as env var C=<code>, never interpolated into argv
        ContainerExecCreateParameters captured = execOps.LastCreateParameters.ShouldNotBeNull();
        string allArgs = string.Join(" ", captured.Cmd);
        allArgs.ShouldNotContain("secret-code");

        IList<string> env = captured.Env.ShouldNotBeNull();
        env.ShouldContain("C=secret-code");
    }

    [Fact]
    public async Task WhenCodeContainsShellMetacharacters_CodeStaysLiteralInEnv()
    {
        // Arrange
        CapturingExecOperations execOps = new();
        DockerWorkerOrchestrator sut = BuildSut(execOps);
        const string codeWithMeta = @"abc$'"";&|";

        // Act
        await sut.DeliverLoginCodeAsync("container-id", codeWithMeta, CancellationToken.None);

        // Assert — the literal code value is in the env var, not shell-expanded
        ContainerExecCreateParameters captured = execOps.LastCreateParameters.ShouldNotBeNull();
        IList<string> env = captured.Env.ShouldNotBeNull();
        env.ShouldContain($"C={codeWithMeta}");

        // The argv must not contain the metacharacters literally (they stay in the env var)
        string allArgs = string.Join(" ", captured.Cmd);
        allArgs.ShouldNotContain(codeWithMeta);
    }

    [Fact]
    public async Task WhenCodeDelivered_AttachStdinIsFalse()
    {
        // Arrange
        CapturingExecOperations execOps = new();
        DockerWorkerOrchestrator sut = BuildSut(execOps);

        // Act
        await sut.DeliverLoginCodeAsync("container-id", "secret-code", CancellationToken.None);

        // Assert — env-var delivery does not use stdin at all
        ContainerExecCreateParameters captured = execOps.LastCreateParameters.ShouldNotBeNull();
        captured.AttachStdin.ShouldBeFalse();
    }

    [Fact]
    public async Task WhenCodeDelivered_NoStreamWriteOccurs()
    {
        // Arrange
        CapturingExecOperations execOps = new();
        DockerWorkerOrchestrator sut = BuildSut(execOps);

        // Act
        await sut.DeliverLoginCodeAsync("container-id", "secret-code", CancellationToken.None);

        // Assert — code delivery no longer writes to the exec stream
        execOps.WrittenStdinBytes.ShouldBeEmpty();
    }

    /// <summary>
    /// Captures exec create parameters and stdin writes for assertion.
    /// </summary>
    private sealed class CapturingExecOperations : IExecOperations
    {
        private const string FakeExecId = "fake-exec-id";

        public ContainerExecCreateParameters? LastCreateParameters { get; private set; }
        public string? LastContainerId { get; private set; }
        public List<byte> WrittenStdinBytes { get; } = [];

        public Task<ContainerExecCreateResponse> ExecCreateContainerAsync(
            string id,
            ContainerExecCreateParameters parameters,
            CancellationToken cancellationToken)
        {
            LastContainerId = id;
            LastCreateParameters = parameters;
            return Task.FromResult(new ContainerExecCreateResponse { ID = FakeExecId });
        }

        public Task<MultiplexedStream> StartAndAttachContainerExecAsync(
            string id,
            bool tty,
            CancellationToken cancellationToken)
        {
            // Return a multiplexed stream backed by a capture stream for stdin
            CapturingStream captureStream = new(WrittenStdinBytes);
            return Task.FromResult(new MultiplexedStream(captureStream, tty));
        }

        public Task<ContainerExecInspectResponse> InspectContainerExecAsync(
            string id,
            CancellationToken cancellationToken)
            => Task.FromResult(new ContainerExecInspectResponse { ExecID = id, Running = false });

        public Task ResizeContainerExecTtyAsync(
            string id,
            ContainerResizeParameters parameters,
            CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task StartContainerExecAsync(string id, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<MultiplexedStream> StartWithConfigContainerExecAsync(
            string id,
            ContainerExecStartParameters eConfig,
            CancellationToken cancellationToken)
            => Task.FromResult(new MultiplexedStream(Stream.Null, false));
    }

    /// <summary>
    /// Captures all bytes written to it (simulates the exec stdin channel).
    /// Also supports reading (returns 0 bytes — simulates closed stdout/stderr from exec).
    /// </summary>
    private sealed class CapturingStream(List<byte> captured) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() { }

        // Return 0 to signal EOF on stdout/stderr (no output from the exec command itself).
        public override int Read(byte[] buffer, int offset, int count) => 0;

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
        {
            for (int i = offset; i < offset + count; i++)
            {
                captured.Add(buffer[i]);
            }
        }
    }

    private sealed class NullContainerOperations : IContainerOperations
    {
        public Task<CreateContainerResponse> CreateContainerAsync(
            CreateContainerParameters parameters,
            CancellationToken cancellationToken)
            => Task.FromResult(new CreateContainerResponse { ID = "null-container" });

        public Task<bool> StartContainerAsync(string id, ContainerStartParameters parameters, CancellationToken cancellationToken)
            => Task.FromResult(true);

        public Task<bool> StopContainerAsync(string id, ContainerStopParameters parameters, CancellationToken cancellationToken)
            => Task.FromResult(true);

        public Task RemoveContainerAsync(string id, ContainerRemoveParameters parameters, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<ContainerInspectResponse> InspectContainerAsync(string id, CancellationToken cancellationToken)
            => Task.FromResult(new ContainerInspectResponse());

        public Task<IList<ContainerListResponse>> ListContainersAsync(ContainersListParameters parameters, CancellationToken cancellationToken)
            => Task.FromResult<IList<ContainerListResponse>>([]);

        public Task<MultiplexedStream> GetContainerLogsAsync(string id, bool tty, ContainerLogsParameters parameters, CancellationToken cancellationToken)
            => Task.FromResult(new MultiplexedStream(Stream.Null, false));

        public Task<MultiplexedStream> AttachContainerAsync(string id, bool tty, ContainerAttachParameters parameters, CancellationToken cancellationToken)
            => Task.FromResult(new MultiplexedStream(Stream.Null, false));

        public Task<Stream> ExportContainerAsync(string id, CancellationToken cancellationToken)
            => Task.FromResult<Stream>(Stream.Null);

        public Task ExtractArchiveToContainerAsync(string id, ContainerPathStatParameters parameters, Stream stream, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<GetArchiveFromContainerResponse> GetArchiveFromContainerAsync(string id, GetArchiveFromContainerParameters parameters, bool statOnly, CancellationToken cancellationToken)
            => Task.FromResult(new GetArchiveFromContainerResponse());

        public Task<Stream> GetContainerLogsAsync(string id, ContainerLogsParameters parameters, CancellationToken cancellationToken)
            => Task.FromResult<Stream>(Stream.Null);

        public Task GetContainerLogsAsync(string id, ContainerLogsParameters parameters, CancellationToken cancellationToken, IProgress<string> progress)
            => Task.CompletedTask;

        public Task<Stream> GetContainerStatsAsync(string id, ContainerStatsParameters parameters, CancellationToken cancellationToken)
            => Task.FromResult<Stream>(Stream.Null);

        public Task GetContainerStatsAsync(string id, ContainerStatsParameters parameters, IProgress<ContainerStatsResponse> progress, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<IList<ContainerFileSystemChangeResponse>> InspectChangesAsync(string id, CancellationToken cancellationToken)
            => Task.FromResult<IList<ContainerFileSystemChangeResponse>>([]);

        public Task KillContainerAsync(string id, ContainerKillParameters parameters, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<ContainerProcessesResponse> ListProcessesAsync(string id, ContainerListProcessesParameters parameters, CancellationToken cancellationToken)
            => Task.FromResult(new ContainerProcessesResponse());

        public Task PauseContainerAsync(string id, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<ContainersPruneResponse> PruneContainersAsync(ContainersPruneParameters parameters, CancellationToken cancellationToken)
            => Task.FromResult(new ContainersPruneResponse());

        public Task RenameContainerAsync(string id, ContainerRenameParameters parameters, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task ResizeContainerTtyAsync(string id, ContainerResizeParameters parameters, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task RestartContainerAsync(string id, ContainerRestartParameters parameters, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task UnpauseContainerAsync(string id, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<ContainerUpdateResponse> UpdateContainerAsync(string id, ContainerUpdateParameters parameters, CancellationToken cancellationToken)
            => Task.FromResult(new ContainerUpdateResponse());

        public Task<ContainerWaitResponse> WaitContainerAsync(string id, CancellationToken cancellationToken)
            => Task.FromResult(new ContainerWaitResponse());
    }

    private sealed class NullVolumeOperations : IVolumeOperations
    {
        public Task<VolumeResponse> CreateAsync(VolumesCreateParameters parameters, CancellationToken cancellationToken)
            => Task.FromResult(new VolumeResponse { Name = parameters.Name });

        public Task<VolumeResponse> InspectAsync(string name, CancellationToken cancellationToken)
            => Task.FromResult(new VolumeResponse { Name = name });

        public Task<VolumesListResponse> ListAsync(CancellationToken cancellationToken)
            => Task.FromResult(new VolumesListResponse());

        public Task<VolumesListResponse> ListAsync(VolumesListParameters parameters, CancellationToken cancellationToken)
            => Task.FromResult(new VolumesListResponse());

        public Task<VolumesPruneResponse> PruneAsync(VolumesPruneParameters parameters, CancellationToken cancellationToken)
            => Task.FromResult(new VolumesPruneResponse());

        public Task RemoveAsync(string name, bool? force, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}
