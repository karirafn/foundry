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

    private sealed class FixedLogsStub(string logContent) : IContainerOperations
    {
        private static MemoryStream BuildRawStream(string content) =>
            new(Encoding.UTF8.GetBytes(content));

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

    private sealed class NotFoundContainerOpsStub : IContainerOperations
    {
        private static DockerContainerNotFoundException BuildNotFoundException() =>
            new(HttpStatusCode.NotFound, "No such container: missing-container");

        public Task<MultiplexedStream> GetContainerLogsAsync(
            string id,
            bool tty,
            ContainerLogsParameters parameters,
            CancellationToken cancellationToken)
            => Task.FromException<MultiplexedStream>(BuildNotFoundException());

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
