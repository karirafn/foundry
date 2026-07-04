using System.Text;

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

public sealed class StreamLogsAsync
{
    private static WorkerOptions DefaultOptions() => new()
    {
        Image = "test-image:latest",
        MemoryLimitMb = 512,
        CpuLimit = 1.5,
        PidsLimit = 256,
    };

    private static DockerWorkerOrchestrator BuildSut(IContainerOperations containerOps) =>
        new(containerOps, Options.Create(DefaultOptions()));

    private static MemoryStream BuildRawStream(string content) =>
        new(Encoding.UTF8.GetBytes(content));

    [Fact]
    public async Task WhenLineContainsHttpsUrlWithUserinfo_UserinfoRedacted()
    {
        // Arrange
        string line = "https://glpat-MySecretToken@gitlab.example.com/owner/repo.git";
        FixedLogsStub stub = new(line);
        DockerWorkerOrchestrator sut = BuildSut(stub);

        // Act
        List<string> lines = [];
        await foreach (string emitted in sut.StreamLogsAsync("container-1", TestContext.Current.CancellationToken)
            .WithCancellation(TestContext.Current.CancellationToken))
        {
            lines.Add(emitted);
        }

        // Assert
        lines.Count.ShouldBe(1);
        lines[0].ShouldNotContain("glpat-MySecretToken");
        lines[0].ShouldContain("https://***@gitlab.example.com");
    }

    [Fact]
    public async Task WhenLineContainsKnownTokenShape_TokenRedacted()
    {
        // Arrange
        string line = "error: Authentication failed for token ghp_abc123DefXyz";
        FixedLogsStub stub = new(line);
        DockerWorkerOrchestrator sut = BuildSut(stub);

        // Act
        List<string> lines = [];
        await foreach (string emitted in sut.StreamLogsAsync("container-2", TestContext.Current.CancellationToken)
            .WithCancellation(TestContext.Current.CancellationToken))
        {
            lines.Add(emitted);
        }

        // Assert
        lines.Count.ShouldBe(1);
        lines[0].ShouldNotContain("ghp_abc123DefXyz");
        lines[0].ShouldContain("***");
    }

    [Fact]
    public async Task WhenLineIsClean_PassesThroughUnchanged()
    {
        // Arrange
        string line = "Cloning into '/workspace'...";
        FixedLogsStub stub = new(line);
        DockerWorkerOrchestrator sut = BuildSut(stub);

        // Act
        List<string> lines = [];
        await foreach (string emitted in sut.StreamLogsAsync("container-3", TestContext.Current.CancellationToken)
            .WithCancellation(TestContext.Current.CancellationToken))
        {
            lines.Add(emitted);
        }

        // Assert
        lines.Count.ShouldBe(1);
        lines[0].ShouldBe(line);
    }

    [Fact]
    public async Task WhenConsumerCancelsMidStream_EnumerationEndsGracefully()
    {
        // Arrange
        using CancellationTokenSource cts = new();
        string firstLine = "first log line";
        BlockingLogsStub stub = new(firstLine);
        DockerWorkerOrchestrator sut = BuildSut(stub);

        // Act
        List<string> lines = [];
        Exception? caught = null;
        try
        {
            await foreach (string emitted in sut.StreamLogsAsync("container-cancel", cts.Token))
            {
                lines.Add(emitted);
                await cts.CancelAsync();
                // An escaping OperationCanceledException here is the failure signal —
                // cancellation propagated unexpectedly instead of ending the enumeration cleanly.
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            caught = ex;
        }

        // Assert
        caught.ShouldBeNull();
        lines.ShouldContain(firstLine);
    }

    [Fact]
    public async Task WhenPumpFaultsWithNonCancellationError_ExceptionPropagates()
    {
        // Arrange
        FaultingLogsStub stub = new(new IOException("simulated pump fault"));
        DockerWorkerOrchestrator sut = BuildSut(stub);

        // Act
        Exception? caught = null;
        try
        {
            await foreach (string _ in sut.StreamLogsAsync("container-fault", CancellationToken.None))
            {
                // consume
            }
        }
        catch (IOException ex)
        {
            caught = ex;
        }

        // Assert
        IOException ioEx = caught.ShouldBeOfType<IOException>();
        ioEx.Message.ShouldBe("simulated pump fault");
    }

    /// <summary>
    /// A stream that emits one line of content then signals EOF on the next read.
    /// After the consumer receives the line and cancels the enumeration token, the
    /// subsequent <see cref="StreamReader.ReadLineAsync"/> call on the already-cancelled
    /// token throws <see cref="OperationCanceledException"/>, which the
    /// <c>ReadLineOrEndAsync</c> helper converts to a clean null end-of-stream.
    /// </summary>
    private sealed class SingleLineStream(string line) : Stream
    {
        private readonly byte[] _content = Encoding.UTF8.GetBytes(line + "\n");
        private int _position;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_position >= _content.Length)
            {
                return 0; // EOF
            }

            int toCopy = Math.Min(count, _content.Length - _position);
            Array.Copy(_content, _position, buffer, offset, toCopy);
            _position += toCopy;
            return toCopy;
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>
    /// A stream that immediately throws a non-cancellation exception on the first read,
    /// simulating a pump fault.
    /// </summary>
    private sealed class FaultingStream(Exception fault) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => throw fault;
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            throw fault;

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>
    /// Base class providing no-op implementations of all <see cref="IContainerOperations"/> members.
    /// Concrete stubs inherit and override only <see cref="GetContainerLogsAsync(string,bool,ContainerLogsParameters,CancellationToken)"/>.
    /// </summary>
    private abstract class NullContainerOperations : IContainerOperations
    {
        public abstract Task<MultiplexedStream> GetContainerLogsAsync(
            string id,
            bool tty,
            ContainerLogsParameters parameters,
            CancellationToken cancellationToken);

        public Task<CreateContainerResponse> CreateContainerAsync(
            CreateContainerParameters parameters,
            CancellationToken cancellationToken)
            => Task.FromResult(new CreateContainerResponse { ID = "stub-id" });

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

    private sealed class BlockingLogsStub(string firstLine) : NullContainerOperations
    {
        public override Task<MultiplexedStream> GetContainerLogsAsync(
            string id,
            bool tty,
            ContainerLogsParameters parameters,
            CancellationToken cancellationToken)
            => Task.FromResult(new MultiplexedStream(new SingleLineStream(firstLine), false));
    }

    private sealed class FaultingLogsStub(Exception fault) : NullContainerOperations
    {
        public override Task<MultiplexedStream> GetContainerLogsAsync(
            string id,
            bool tty,
            ContainerLogsParameters parameters,
            CancellationToken cancellationToken)
            => Task.FromResult(new MultiplexedStream(new FaultingStream(fault), false));
    }

    private sealed class FixedLogsStub(string logContent) : NullContainerOperations
    {
        public override Task<MultiplexedStream> GetContainerLogsAsync(
            string id,
            bool tty,
            ContainerLogsParameters parameters,
            CancellationToken cancellationToken)
            => Task.FromResult(new MultiplexedStream(BuildRawStream(logContent), false));
    }
}
