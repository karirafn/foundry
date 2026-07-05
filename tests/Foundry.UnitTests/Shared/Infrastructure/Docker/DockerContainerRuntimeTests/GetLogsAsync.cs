using System.Net;
using System.Text;

using Docker.DotNet;
using Docker.DotNet.Models;

using Foundry.Shared.Infrastructure.Docker;
using Foundry.UnitTests.Fakes.Workers;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Shared.Infrastructure.Docker.DockerContainerRuntimeTests;
#pragma warning disable CA1707 // underscores in test names

public sealed class GetLogsAsync
{
    private static DockerContainerRuntime BuildSut(IContainerOperations containerOps) =>
        new(containerOps, new NullVolumeOperations(), new NullExecOperations());

    [Fact]
    public async Task WhenContainerNotFound_ReturnsNull()
    {
        // Arrange
        NotFoundContainerOpsStub stub = new();
        DockerContainerRuntime sut = BuildSut(stub);

        // Act
        string? result = await sut.GetLogsAsync("missing-container", 100, CancellationToken.None);

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public async Task WhenOutputIsClean_ReturnsRawContentUnredacted()
    {
        // Arrange — the runtime must NOT redact; that policy belongs to the orchestrator
        string raw = "Cloning into '/workspace'...\nDone.";
        FixedLogsStub stub = new(raw);
        DockerContainerRuntime sut = BuildSut(stub);

        // Act
        string? result = await sut.GetLogsAsync("container-1", 500, CancellationToken.None);

        // Assert
        result.ShouldBe(raw);
    }

    [Fact]
    public async Task WhenOutputContainsHttpsUrlWithUserinfo_UserinfoPassesThroughUnredacted()
    {
        // Arrange — transport layer must return secrets verbatim; redaction is orchestrator policy
        string raw = "https://glpat-MySecretToken@gitlab.example.com/owner/repo.git";
        FixedLogsStub stub = new(raw);
        DockerContainerRuntime sut = BuildSut(stub);

        // Act
        string? result = await sut.GetLogsAsync("container-2", 500, CancellationToken.None);

        // Assert
        result.ShouldNotBeNull();
        result.ShouldContain("glpat-MySecretToken");
    }

    [Fact]
    public async Task WhenOutputContainsKnownTokenShape_TokenPassesThroughUnredacted()
    {
        // Arrange
        string raw = "error: Authentication failed for token ghp_abc123DefXyz";
        FixedLogsStub stub = new(raw);
        DockerContainerRuntime sut = BuildSut(stub);

        // Act
        string? result = await sut.GetLogsAsync("container-3", 500, CancellationToken.None);

        // Assert
        result.ShouldNotBeNull();
        result.ShouldContain("ghp_abc123DefXyz");
        result.ShouldNotContain("***");
    }

    // ── Stubs ────────────────────────────────────────────────────────────────

    private sealed class FixedLogsStub(string logContent) : StubContainerOpsBase
    {
        private static MemoryStream BuildRawStream(string content) =>
            new(Encoding.UTF8.GetBytes(content));

        public override Task<MultiplexedStream> GetContainerLogsAsync(
            string id,
            bool tty,
            ContainerLogsParameters parameters,
            CancellationToken cancellationToken)
            => Task.FromResult(new MultiplexedStream(BuildRawStream(logContent), false));
    }

    private sealed class NotFoundContainerOpsStub : StubContainerOpsBase
    {
        private static DockerContainerNotFoundException BuildNotFoundException() =>
            new(HttpStatusCode.NotFound, "No such container: missing-container");

        public override Task<MultiplexedStream> GetContainerLogsAsync(
            string id,
            bool tty,
            ContainerLogsParameters parameters,
            CancellationToken cancellationToken)
            => Task.FromException<MultiplexedStream>(BuildNotFoundException());
    }
}
