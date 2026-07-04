using System.Text;

using Docker.DotNet;
using Docker.DotNet.Models;

using Foundry.Modules.Workers.Domain;
using Foundry.Modules.Workers.Features;
using Foundry.Modules.Workers.Infrastructure;
using Foundry.Shared;
using Foundry.UnitTests.Fakes.Workers;

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
        new(containerOps, new NullVolumeOperations(), new NullExecOperations(), Options.Create(DefaultOptions()));

    // ── EOF / early-break resource-cleanup tests ──────────────────────────────

    [Fact]
    public async Task WhenPumpThrowsUnexpectedIoException_ExceptionSurfacesFromEnumeration()
    {
        // Arrange — the stream throws an IOException that is NOT the known end-of-stream variant.
        // This must propagate from StreamLogsAsync rather than being swallowed.
        ThrowingStreamStub stub = new(new IOException("Network reset by peer"));
        DockerWorkerOrchestrator sut = BuildSut(stub);

        // Act
        Exception? thrown = null;
        try
        {
            await foreach (string line in sut.StreamLogsAsync(
                "container-io-error",
                TestContext.Current.CancellationToken))
            {
                _ = line;
            }
        }
#pragma warning disable CA1031 // Intentionally catch-all to assert the specific exception type propagates
        catch (Exception ex)
#pragma warning restore CA1031
        {
            thrown = ex;
        }

        // Assert — the unexpected IOException must reach the consumer
        Exception notNull = thrown.ShouldNotBeNull();
        IOException ioEx = notNull.ShouldBeOfType<IOException>();
        ioEx.Message.ShouldBe("Network reset by peer");
    }

    [Fact]
    public async Task WhenStreamThrowsUnexpectedEndOfStream_EnumerationCompletesWithoutException()
    {
        // Arrange — stream throws the IOException that Docker emits when the container exits
        // and CopyOutputToAsync is still trying to read the next frame header.
        ThrowingStreamStub stub = new(new IOException("Unexpected end of stream"));
        DockerWorkerOrchestrator sut = BuildSut(stub);

        // Act
        List<string> lines = [];
        Exception? thrown = null;
        try
        {
            await foreach (string line in sut.StreamLogsAsync(
                "container-eof",
                TestContext.Current.CancellationToken))
            {
                lines.Add(line);
            }
        }
#pragma warning disable CA1031 // Intentionally catch-all to assert no exception propagates from StreamLogsAsync
        catch (Exception ex)
#pragma warning restore CA1031
        {
            thrown = ex;
        }

        // Assert — end-of-stream must not surface to the consumer
        thrown.ShouldBeNull();
    }

    [Fact]
    public async Task WhenStreamThrowsEndOfStreamException_EnumerationCompletesWithoutException()
    {
        // Arrange — alternate form: EndOfStreamException instead of IOException
        ThrowingStreamStub stub = new(new EndOfStreamException("Stream ended"));
        DockerWorkerOrchestrator sut = BuildSut(stub);

        // Act
        Exception? thrown = null;
        try
        {
            await foreach (string line in sut.StreamLogsAsync(
                "container-eos",
                TestContext.Current.CancellationToken))
            {
                _ = line;
            }
        }
#pragma warning disable CA1031 // Intentionally catch-all to assert no exception propagates from StreamLogsAsync
        catch (Exception ex)
#pragma warning restore CA1031
        {
            thrown = ex;
        }

        // Assert
        thrown.ShouldBeNull();
    }

    [Fact]
    public async Task WhenConsumerCancelsMidStream_EnumerationEndsCleanlyWithNoOce()
    {
        // Arrange — stream emits one line then blocks. The consumer cancels after receiving
        // the first line. Enumeration must complete without an OperationCanceledException
        // escaping the iterator.
        TwoPhaseStreamStub stub = new("first line\n");
        DockerWorkerOrchestrator sut = BuildSut(stub);
        using CancellationTokenSource cts = new();

        // Act
        List<string> received = [];
        Exception? thrown = null;
        try
        {
            await foreach (string line in sut.StreamLogsAsync("container-cancel", cts.Token))
            {
                received.Add(line);
                await cts.CancelAsync();
            }
        }
#pragma warning disable CA1031 // Intentionally catch-all to assert no exception propagates from StreamLogsAsync
        catch (Exception ex)
#pragma warning restore CA1031
        {
            thrown = ex;
        }

        // Assert — first line received, no OCE escapes
        received.Count.ShouldBe(1);
        received[0].ShouldBe("first line");
        thrown.ShouldBeNull();
    }

    [Fact]
    public async Task WhenConsumerBreaksEarly_PumpIsSignalledViaCancellationNotDisposal()
    {
        // Arrange — stream yields a line, then blocks. The stub records whether the blocking
        // phase ended via OperationCanceledException (correct: linked CTS fired) or via
        // ObjectDisposedException (broken: stream was disposed without prior cancellation).
        TwoPhaseStreamStub stub = new("line from container\n");
        DockerWorkerOrchestrator sut = BuildSut(stub);

        // Act — consume one line then break (mirrors WaitForLoginSuccessAsync / ScanForUrlAsync)
        await foreach (string line in sut.StreamLogsAsync(
            "container-break",
            TestContext.Current.CancellationToken))
        {
            _ = line;
            break;
        }

        // Give the background pump a moment to observe the cancellation signal.
        bool pumped = await Task.WhenAny(
            stub.BlockingPhaseResult,
            Task.Delay(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken))
            == stub.BlockingPhaseResult;

        // Assert
        pumped.ShouldBeTrue("pump must acknowledge cancellation within 5 s after consumer breaks");
        bool cancelledCleanly = await stub.BlockingPhaseResult;
        cancelledCleanly.ShouldBeTrue(
            "pump must receive OperationCanceledException (linked CTS fired) before the stream is disposed; " +
            "false means the stream was disposed without prior cancellation (orphaned-task bug)");
    }

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

    private sealed class FixedLogsStub(string logContent) : IContainerOperations
    {
        public Task<MultiplexedStream> GetContainerLogsAsync(
            string id,
            bool tty,
            ContainerLogsParameters parameters,
            CancellationToken cancellationToken)
            => Task.FromResult(new MultiplexedStream(BuildRawStream(logContent), false));

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

    // ── Resource-cleanup stubs ────────────────────────────────────────────────

    /// <summary>
    /// Returns a <see cref="MultiplexedStream"/> backed by a stream that throws the supplied
    /// exception on the first read — simulating Docker's
    /// <c>CopyOutputToAsync</c> throwing "Unexpected end of stream" when the container exits.
    /// </summary>
    private sealed class ThrowingStreamStub(Exception exceptionToThrow) : StubContainerOpsBase
    {
        public override Task<MultiplexedStream> GetContainerLogsAsync(
            string id,
            bool tty,
            ContainerLogsParameters parameters,
            CancellationToken cancellationToken)
            => Task.FromResult(new MultiplexedStream(new ThrowingStream(exceptionToThrow), false));

        private sealed class ThrowingStream(Exception exception) : Stream
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

            public override int Read(byte[] buffer, int offset, int count) =>
                throw exception;

            public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken) =>
                ValueTask.FromException<int>(exception);

            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
                Task.FromException<int>(exception);

            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }
    }

    /// <summary>
    /// Returns a <see cref="MultiplexedStream"/> backed by a two-phase stream: the first
    /// phase emits <paramref name="firstLineContent"/> bytes, then the second phase blocks
    /// until the read is cancelled or the stream is disposed.
    /// <para>
    /// <see cref="BlockingPhaseResult"/> resolves to <c>true</c> when the blocking phase
    /// ended via <see cref="OperationCanceledException"/> (linked CTS fired — correct behavior)
    /// and <c>false</c> when it ended via <see cref="ObjectDisposedException"/> (stream was
    /// disposed without prior cancellation — orphaned-task bug).
    /// </para>
    /// </summary>
    private sealed class TwoPhaseStreamStub(string firstLineContent) : StubContainerOpsBase
    {
        private readonly TaskCompletionSource<bool> _blockingPhaseResult =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        /// Completes when the blocking phase of the stream ends; <c>true</c> = cancelled
        /// cleanly (OCE), <c>false</c> = disposed without cancellation.
        /// </summary>
        public Task<bool> BlockingPhaseResult => _blockingPhaseResult.Task;

        public override Task<MultiplexedStream> GetContainerLogsAsync(
            string id,
            bool tty,
            ContainerLogsParameters parameters,
            CancellationToken cancellationToken)
            => Task.FromResult(new MultiplexedStream(
                new TwoPhaseStream(Encoding.UTF8.GetBytes(firstLineContent), _blockingPhaseResult),
                false));

        private sealed class TwoPhaseStream(byte[] firstPhase, TaskCompletionSource<bool> result) : Stream
        {
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

            public override int Read(byte[] buffer, int offset, int count) =>
                throw new NotSupportedException("Use async overload");

            public override async Task<int> ReadAsync(
                byte[] buffer,
                int offset,
                int count,
                CancellationToken cancellationToken)
            {
                if (_position < firstPhase.Length)
                {
                    int toCopy = Math.Min(count, firstPhase.Length - _position);
                    Array.Copy(firstPhase, _position, buffer, offset, toCopy);
                    _position += toCopy;
                    return toCopy;
                }

                // Blocking phase — wait until cancelled or disposed.
                try
                {
                    await Task.Delay(Timeout.Infinite, cancellationToken);
                    return 0;
                }
                catch (OperationCanceledException)
                {
                    result.TrySetResult(true);
                    throw;
                }
            }

            public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
            {
                if (_position < firstPhase.Length)
                {
                    int toCopy = Math.Min(buffer.Length, firstPhase.Length - _position);
                    firstPhase.AsMemory(_position, toCopy).CopyTo(buffer);
                    _position += toCopy;
                    return toCopy;
                }

                try
                {
                    await Task.Delay(Timeout.Infinite, cancellationToken);
                    return 0;
                }
                catch (OperationCanceledException)
                {
                    result.TrySetResult(true);
                    throw;
                }
            }

            protected override void Dispose(bool disposing)
            {
                // Signal disposal without prior cancellation (the bug scenario).
                result.TrySetResult(false);
                base.Dispose(disposing);
            }

            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }
    }

    /// <summary>
    /// Base class providing no-op implementations of all <see cref="IContainerOperations"/>
    /// members except <see cref="GetContainerLogsAsync(string,bool,ContainerLogsParameters,CancellationToken)"/>
    /// so stubs only need to override what they care about.
    /// </summary>
    private abstract class StubContainerOpsBase : IContainerOperations
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
}
