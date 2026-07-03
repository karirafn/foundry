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

public sealed class GetLogsAsync
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

    [Fact]
    public async Task WhenOutputContainsHttpsUrlWithUserinfo_UserinfoRedacted()
    {
        // Arrange
        string raw = "Cloning into '/workspace'...\nhttps://glpat-MySecretToken@gitlab.example.com/owner/repo.git";
        FixedLogsStub stub = new(raw);
        DockerWorkerOrchestrator sut = BuildSut(stub);

        // Act
        string? result = await sut.GetLogsAsync("container-1", 500, CancellationToken.None);

        // Assert
        result.ShouldNotBeNull();
        result.ShouldNotContain("glpat-MySecretToken");
        result.ShouldContain("https://***@gitlab.example.com");
    }

    [Fact]
    public async Task WhenOutputContainsKnownTokenShape_TokenRedacted()
    {
        // Arrange
        string raw = "error: Authentication failed for token ghp_abc123DefXyz";
        FixedLogsStub stub = new(raw);
        DockerWorkerOrchestrator sut = BuildSut(stub);

        // Act
        string? result = await sut.GetLogsAsync("container-2", 500, CancellationToken.None);

        // Assert
        result.ShouldNotBeNull();
        result.ShouldNotContain("ghp_abc123DefXyz");
        result.ShouldContain("***");
    }

    [Fact]
    public async Task WhenOutputIsClean_PassesThroughUnchanged()
    {
        // Arrange
        string raw = "Cloning into '/workspace'...\nDone.";
        FixedLogsStub stub = new(raw);
        DockerWorkerOrchestrator sut = BuildSut(stub);

        // Act
        string? result = await sut.GetLogsAsync("container-3", 500, CancellationToken.None);

        // Assert
        result.ShouldBe(raw);
    }

    [Fact]
    public async Task WhenSecretNearTruncationBoundary_SecretRedactedBeforeTruncation()
    {
        // Arrange — put secret at position 65500 (within the 65536-byte window)
        // If truncation happened before redaction, the secret could survive.
        string prefix = new('a', 65_500);
        string raw = prefix + " ghp_SecretNearBoundary extra padding to exceed 65536";
        FixedLogsStub stub = new(raw);
        DockerWorkerOrchestrator sut = BuildSut(stub);

        // Act
        string? result = await sut.GetLogsAsync("container-4", 500, CancellationToken.None);

        // Assert
        result.ShouldNotBeNull();
        result.ShouldNotContain("ghp_SecretNearBoundary");
    }

    [Fact]
    public async Task WhenSecretPrefixStraddlesByteWindow_ValueIsRedacted()
    {
        // Arrange — token sits at the very start of the output, followed by >65_519 bytes
        // of padding. The old code seeks to (total_bytes - 65_536), which puts the read
        // cursor at byte 1 — cutting off the 'g' in "ghp_" so the tail string begins with
        // "hp_StraddleToken..." and the ghp_\S+ regex never matches, leaking the value.
        // The fix must redact the FULL buffer first, then keep the tail.
        string suffix = new('b', 65_520);                    // 65_520 padding bytes after token
        string raw = "ghp_StraddleToken" + suffix;           // total = 65_537 chars; window start = byte 1
        FixedLogsStub stub = new(raw);
        DockerWorkerOrchestrator sut = BuildSut(stub);

        // Act
        string? result = await sut.GetLogsAsync("container-5", 500, CancellationToken.None);

        // Assert
        result.ShouldNotBeNull();
        result.ShouldNotContain("StraddleToken");
    }

    private sealed class FixedLogsStub(string logContent) : IContainerOperations
    {
        public Task<MultiplexedStream> GetContainerLogsAsync(
            string id,
            bool tty,
            ContainerLogsParameters parameters,
            CancellationToken cancellationToken)
            => Task.FromResult(new MultiplexedStream(new MemoryStream(Encoding.UTF8.GetBytes(logContent)), false));

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
}
