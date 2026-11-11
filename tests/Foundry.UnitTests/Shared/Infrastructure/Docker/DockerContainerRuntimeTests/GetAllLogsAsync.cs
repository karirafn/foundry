using System.Text;

using Docker.DotNet;
using Docker.DotNet.Models;

using Foundry.Shared.Infrastructure.Docker;
using Foundry.UnitTests.Fakes.Workers;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Shared.Infrastructure.Docker.DockerContainerRuntimeTests;

public sealed class GetAllLogsAsync
{
    [Fact]
    public async Task WhenCalled_RequestsTheWholeLogWithTimestamps()
    {
        // Arrange
        RecordingLogsStub stub = new("line one\nline two");
        DockerContainerRuntime sut = new(stub, new NullVolumeOperations(), new NullExecOperations());

        // Act
        string? result = await sut.GetAllLogsAsync("container-1", CancellationToken.None);

        // Assert
        result.ShouldSatisfyAllConditions(
            () => result.ShouldBe("line one\nline two"),
            () => stub.LastParameters.ShouldNotBeNull().Tail.ShouldBe("all"),
            () => stub.LastParameters!.Timestamps.ShouldBe(true));
    }

    private sealed class RecordingLogsStub(string logContent) : StubContainerOpsBase
    {
        public ContainerLogsParameters? LastParameters { get; private set; }

        public override Task<MultiplexedStream> GetContainerLogsAsync(
            string id,
            bool tty,
            ContainerLogsParameters parameters,
            CancellationToken cancellationToken)
        {
            LastParameters = parameters;
            return Task.FromResult(new MultiplexedStream(new MemoryStream(Encoding.UTF8.GetBytes(logContent)), false));
        }
    }
}
