using Foundry.Modules.Workers.Features.Runs;
using Foundry.Testing;
using Foundry.WebApi.Hubs;

using Microsoft.AspNetCore.SignalR;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Hubs.WorkerHubTests;

public sealed class StreamLog
{
    private sealed class StubWorkerLogStream : IWorkerLogStream
    {
        private readonly IReadOnlyList<string> _lines;
        public Guid? LastWorkerRunId { get; private set; }

        public StubWorkerLogStream(IReadOnlyList<string> lines)
        {
            _lines = lines;
        }

        public async IAsyncEnumerable<string> StreamAsync(
            Guid workerRunId,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            LastWorkerRunId = workerRunId;

            foreach (string line in _lines)
            {
                yield return line;
                await Task.Yield();
            }
        }
    }

    [Fact]
    public void WhenCreated_InheritsFromHub()
    {
        // Arrange
        StubWorkerLogStream stub = new([]);

        // Act
        WorkerHub sut = new(stub, new NullWorkerRunQueries());

        // Assert
        sut.ShouldBeAssignableTo<Hub>();
    }

    [Fact]
    public async Task WhenStreamLogCalled_DelegatesWorkerRunIdToLogStream()
    {
        // Arrange
        Guid workerRunId = Guid.NewGuid();
        StubWorkerLogStream stub = new(["line one"]);
        WorkerHub sut = new(stub, new NullWorkerRunQueries());

        // Act
        List<string> received = [];
        await foreach (string line in sut.StreamLog(workerRunId, CancellationToken.None))
        {
            received.Add(line);
        }

        // Assert
        stub.LastWorkerRunId.ShouldBe(workerRunId);
    }

    [Fact]
    public async Task WhenStreamLogCalled_YieldsLinesFromLogStream()
    {
        // Arrange
        Guid workerRunId = Guid.NewGuid();
        IReadOnlyList<string> expectedLines = ["line one", "line two"];
        StubWorkerLogStream stub = new(expectedLines);
        WorkerHub sut = new(stub, new NullWorkerRunQueries());

        // Act
        List<string> received = [];
        await foreach (string line in sut.StreamLog(workerRunId, CancellationToken.None))
        {
            received.Add(line);
        }

        // Assert
        received.ShouldBe(expectedLines);
    }
}
