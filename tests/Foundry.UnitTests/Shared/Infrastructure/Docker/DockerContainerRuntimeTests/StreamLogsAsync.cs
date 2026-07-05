using System.Text;

using Docker.DotNet;
using Docker.DotNet.Models;

using Foundry.Shared.Infrastructure.Docker;
using Foundry.UnitTests.Fakes.Workers;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Shared.Infrastructure.Docker.DockerContainerRuntimeTests;
#pragma warning disable CA1707 // underscores in test names

public sealed class StreamLogsAsync
{
    private static DockerContainerRuntime BuildSut(IContainerOperations containerOps) =>
        new(containerOps, new NullVolumeOperations(), new NullExecOperations());

    // ── Policy-free transport proofs ─────────────────────────────────────────

    [Fact]
    public async Task WhenLineContainsHttpsUrlWithUserinfo_UserinfoPassesThroughUnredacted()
    {
        // Arrange — the runtime must NOT redact; that policy belongs to the orchestrator
        string line = "https://glpat-MySecretToken@gitlab.example.com/owner/repo.git";
        FixedLogsStub stub = new(line);
        DockerContainerRuntime sut = BuildSut(stub);

        // Act
        List<string> lines = [];
        await foreach (string emitted in sut.StreamLogsAsync("container-1", TestContext.Current.CancellationToken)
            .WithCancellation(TestContext.Current.CancellationToken))
        {
            lines.Add(emitted);
        }

        // Assert — secret token must arrive UNredacted at the transport layer
        lines.Count.ShouldBe(1);
        lines[0].ShouldContain("glpat-MySecretToken");
    }

    [Fact]
    public async Task WhenLineContainsKnownTokenShape_TokenPassesThroughUnredacted()
    {
        // Arrange — runtime layer is policy-free; no redaction of any kind
        string line = "error: Authentication failed for token ghp_abc123DefXyz";
        FixedLogsStub stub = new(line);
        DockerContainerRuntime sut = BuildSut(stub);

        // Act
        List<string> lines = [];
        await foreach (string emitted in sut.StreamLogsAsync("container-2", TestContext.Current.CancellationToken)
            .WithCancellation(TestContext.Current.CancellationToken))
        {
            lines.Add(emitted);
        }

        // Assert — token must not be replaced with *** at the transport layer
        lines.Count.ShouldBe(1);
        lines[0].ShouldContain("ghp_abc123DefXyz");
        lines[0].ShouldNotContain("***");
    }

    [Fact]
    public async Task WhenLineIsClean_PassesThroughUnchanged()
    {
        // Arrange
        string line = "Cloning into '/workspace'...";
        FixedLogsStub stub = new(line);
        DockerContainerRuntime sut = BuildSut(stub);

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

    // ── EOF / exception-normalization tests ─────────────────────────────────

    [Fact]
    public async Task WhenPumpThrowsUnexpectedIoException_ExceptionSurfacesFromEnumeration()
    {
        // Arrange
        ThrowingStreamStub stub = new(new IOException("Network reset by peer"));
        DockerContainerRuntime sut = BuildSut(stub);

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

        // Assert — unexpected IOException must reach the consumer
        Exception notNull = thrown.ShouldNotBeNull();
        IOException ioEx = notNull.ShouldBeOfType<IOException>();
        ioEx.Message.ShouldBe("Network reset by peer");
    }

    [Fact]
    public async Task WhenStreamThrowsUnexpectedEndOfStream_EnumerationCompletesWithoutException()
    {
        // Arrange — known end-of-stream variant must not propagate
        ThrowingStreamStub stub = new(new IOException("Unexpected end of stream"));
        DockerContainerRuntime sut = BuildSut(stub);

        // Act
        Exception? thrown = null;
        try
        {
            await foreach (string line in sut.StreamLogsAsync(
                "container-eof",
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
    public async Task WhenStreamThrowsEndOfStreamException_EnumerationCompletesWithoutException()
    {
        // Arrange
        ThrowingStreamStub stub = new(new EndOfStreamException("Stream ended"));
        DockerContainerRuntime sut = BuildSut(stub);

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
        // Arrange — stream emits one line then blocks; consumer cancels after receiving it
        TwoPhaseStreamStub stub = new("first line\n");
        DockerContainerRuntime sut = BuildSut(stub);
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
        // Arrange — stub records whether blocking phase ended via OCE (correct) or ObjectDisposedException (bug)
        TwoPhaseStreamStub stub = new("line from container\n");
        DockerContainerRuntime sut = BuildSut(stub);

        // Act — consume one line then break
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

    // ── Stubs ────────────────────────────────────────────────────────────────

    private static MemoryStream BuildRawStream(string content) =>
        new(Encoding.UTF8.GetBytes(content));

    private sealed class FixedLogsStub(string logContent) : StubContainerOpsBase
    {
        public override Task<MultiplexedStream> GetContainerLogsAsync(
            string id,
            bool tty,
            ContainerLogsParameters parameters,
            CancellationToken cancellationToken)
            => Task.FromResult(new MultiplexedStream(BuildRawStream(logContent), false));
    }

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

    private sealed class TwoPhaseStreamStub(string firstLineContent) : StubContainerOpsBase
    {
        private readonly TaskCompletionSource<bool> _blockingPhaseResult =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

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
                result.TrySetResult(false);
                base.Dispose(disposing);
            }

            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }
    }

}
