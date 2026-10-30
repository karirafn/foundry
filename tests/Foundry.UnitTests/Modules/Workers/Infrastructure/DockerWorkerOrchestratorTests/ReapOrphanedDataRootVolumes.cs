using Docker.DotNet;
using Docker.DotNet.Models;

using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features;
using Foundry.Modules.Workers.Features.Orchestration;
using Foundry.Modules.Workers.Infrastructure;
using Foundry.Shared.Infrastructure.Docker;
using Foundry.UnitTests.Fakes.Workers;

using Microsoft.Extensions.Options;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Infrastructure.DockerWorkerOrchestratorTests;

public sealed class ReapOrphanedDataRootVolumes
{
    private static WorkerOptions DefaultOptions() => new()
    {
        MemoryLimitMb = 512,
        CpuLimit = 1.5,
        PidsLimit = 256,
    };

    private static DockerWorkerOrchestrator BuildSut(FakeDockerContainerRuntime runtime) =>
        new(runtime, Options.Create(DefaultOptions()));

    private static IReadOnlyDictionary<string, string> VolumeLabels(WorkerRunId runId) =>
        new Dictionary<string, string>
        {
            ["foundry.worker-run-id"] = runId.Value.ToString(),
        };

    [Fact]
    public async Task WhenVolumeRunIdNotInActiveSet_RemovesVolume()
    {
        // Arrange
        WorkerRunId orphanedRunId = WorkerRunId.New();
        string volumeName = WorkerVolumeNames.DataRootVolumeName(orphanedRunId);

        FakeDockerContainerRuntime runtime = new FakeDockerContainerRuntime()
            .WithVolumeListResponse([(volumeName, VolumeLabels(orphanedRunId))]);

        DockerWorkerOrchestrator sut = BuildSut(runtime);
        HashSet<WorkerRunId> activeRunIds = [];

        // Act
        await sut.ReapOrphanedDataRootVolumesAsync(activeRunIds, CancellationToken.None);

        // Assert
        runtime.AllRemovedVolumeNames.ShouldContain(volumeName);
    }

    [Fact]
    public async Task WhenVolumeRunIdIsInActiveSet_DoesNotRemoveVolume()
    {
        // Arrange
        WorkerRunId activeRunId = WorkerRunId.New();
        string volumeName = WorkerVolumeNames.DataRootVolumeName(activeRunId);

        FakeDockerContainerRuntime runtime = new FakeDockerContainerRuntime()
            .WithVolumeListResponse([(volumeName, VolumeLabels(activeRunId))]);

        DockerWorkerOrchestrator sut = BuildSut(runtime);
        HashSet<WorkerRunId> activeRunIds = [activeRunId];

        // Act
        await sut.ReapOrphanedDataRootVolumesAsync(activeRunIds, CancellationToken.None);

        // Assert
        runtime.AllRemovedVolumeNames.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenVolumeLabelMissingRunId_SkipsVolume()
    {
        // Arrange
        FakeDockerContainerRuntime runtime = new FakeDockerContainerRuntime()
            .WithVolumeListResponse([("foundry-worker-docker-nolabel", new Dictionary<string, string>())]);

        DockerWorkerOrchestrator sut = BuildSut(runtime);
        HashSet<WorkerRunId> activeRunIds = [];

        // Act
        await sut.ReapOrphanedDataRootVolumesAsync(activeRunIds, CancellationToken.None);

        // Assert
        runtime.AllRemovedVolumeNames.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenVolumeLabelHasUnparseableGuid_SkipsVolume()
    {
        // Arrange
        FakeDockerContainerRuntime runtime = new FakeDockerContainerRuntime()
            .WithVolumeListResponse(
            [
                (
                    "foundry-worker-docker-notvalid",
                    new Dictionary<string, string> { ["foundry.worker-run-id"] = "not-a-guid" }
                ),
            ]);

        DockerWorkerOrchestrator sut = BuildSut(runtime);
        HashSet<WorkerRunId> activeRunIds = [];

        // Act
        await sut.ReapOrphanedDataRootVolumesAsync(activeRunIds, CancellationToken.None);

        // Assert
        runtime.AllRemovedVolumeNames.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenOneRemoveThrows_ContinuesRemovingRemainingOrphanedVolumes()
    {
        // Arrange
        WorkerRunId orphan1 = WorkerRunId.New();
        WorkerRunId orphan2 = WorkerRunId.New();
        string volumeName1 = WorkerVolumeNames.DataRootVolumeName(orphan1);
        string volumeName2 = WorkerVolumeNames.DataRootVolumeName(orphan2);

        // Use a runtime that throws on the first removal but succeeds on subsequent ones.
        ThrowOnFirstRemoveRuntime runtime = new(
            [(volumeName1, VolumeLabels(orphan1)), (volumeName2, VolumeLabels(orphan2))]);

        // Build the SUT using the ThrowOnFirstRemoveRuntime typed as IDockerContainerRuntime.
        DockerWorkerOrchestrator sut = new(runtime, Options.Create(DefaultOptions()));
        HashSet<WorkerRunId> activeRunIds = [];

        // Act — must not throw despite one remove failing
        await Should.NotThrowAsync(
            () => sut.ReapOrphanedDataRootVolumesAsync(activeRunIds, CancellationToken.None));

        // Assert — the second volume was still attempted
        runtime.RemoveCallCount.ShouldBe(2);
    }

    [Fact]
    public async Task WhenVolumeListIsEmpty_NoRemovesAreAttempted()
    {
        // Arrange
        FakeDockerContainerRuntime runtime = new FakeDockerContainerRuntime()
            .WithVolumeListResponse([]);

        DockerWorkerOrchestrator sut = BuildSut(runtime);
        HashSet<WorkerRunId> activeRunIds = [];

        // Act
        await sut.ReapOrphanedDataRootVolumesAsync(activeRunIds, CancellationToken.None);

        // Assert
        runtime.AllRemovedVolumeNames.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenMixedActiveAndOrphanedVolumes_OnlyRemovesOrphaned()
    {
        // Arrange
        WorkerRunId activeRunId = WorkerRunId.New();
        WorkerRunId orphanedRunId = WorkerRunId.New();
        string activeVolumeName = WorkerVolumeNames.DataRootVolumeName(activeRunId);
        string orphanedVolumeName = WorkerVolumeNames.DataRootVolumeName(orphanedRunId);

        FakeDockerContainerRuntime runtime = new FakeDockerContainerRuntime()
            .WithVolumeListResponse(
            [
                (activeVolumeName, VolumeLabels(activeRunId)),
                (orphanedVolumeName, VolumeLabels(orphanedRunId)),
            ]);

        DockerWorkerOrchestrator sut = BuildSut(runtime);
        HashSet<WorkerRunId> activeRunIds = [activeRunId];

        // Act
        await sut.ReapOrphanedDataRootVolumesAsync(activeRunIds, CancellationToken.None);

        // Assert
        runtime.AllRemovedVolumeNames.ShouldHaveSingleItem();
        runtime.AllRemovedVolumeNames.ShouldContain(orphanedVolumeName);
        runtime.AllRemovedVolumeNames.ShouldNotContain(activeVolumeName);
    }

    [Fact]
    public async Task WhenCalled_QueriesVolumesByWorkerRunIdLabelKey()
    {
        // Arrange
        FakeDockerContainerRuntime runtime = new FakeDockerContainerRuntime()
            .WithVolumeListResponse([]);

        DockerWorkerOrchestrator sut = BuildSut(runtime);
        HashSet<WorkerRunId> activeRunIds = [];

        // Act
        await sut.ReapOrphanedDataRootVolumesAsync(activeRunIds, CancellationToken.None);

        // Assert
        runtime.LastListVolumesByLabelKey.ShouldBe("foundry.worker-run-id");
    }

    /// <summary>
    /// A minimal runtime that throws on the first RemoveVolumeAsync call and
    /// counts all subsequent calls, used to verify best-effort per-volume removal.
    /// Delegates all other methods to an inner <see cref="FakeDockerContainerRuntime"/>.
    /// </summary>
    private sealed class ThrowOnFirstRemoveRuntime : IDockerContainerRuntime
    {
        private readonly FakeDockerContainerRuntime _inner;
        private bool _hasThrown;

        public int RemoveCallCount { get; private set; }

        public ThrowOnFirstRemoveRuntime(
            IReadOnlyList<(string Name, IReadOnlyDictionary<string, string> Labels)> volumes)
        {
            _inner = new FakeDockerContainerRuntime().WithVolumeListResponse(volumes);
        }

        public Task RemoveVolumeAsync(string name, bool force, CancellationToken cancellationToken)
        {
            RemoveCallCount++;

            if (!_hasThrown)
            {
                _hasThrown = true;
                throw new DockerApiException(
                    System.Net.HttpStatusCode.InternalServerError,
                    "Simulated Docker remove failure");
            }

            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<(string Name, IReadOnlyDictionary<string, string> Labels)>> ListVolumesByLabelAsync(
            string labelKey,
            CancellationToken cancellationToken)
            => _inner.ListVolumesByLabelAsync(labelKey, cancellationToken);

        public Task<string> CreateAndStartAsync(
            CreateContainerParameters createParams,
            CancellationToken cancellationToken)
            => _inner.CreateAndStartAsync(createParams, cancellationToken);

        public Task StopAsync(string containerId, int waitBeforeKillSeconds, CancellationToken cancellationToken)
            => _inner.StopAsync(containerId, waitBeforeKillSeconds, cancellationToken);

        public Task RemoveAsync(string containerId, CancellationToken cancellationToken)
            => _inner.RemoveAsync(containerId, cancellationToken);

        public Task<ContainerInspectResponse> InspectAsync(
            string containerId,
            CancellationToken cancellationToken)
            => _inner.InspectAsync(containerId, cancellationToken);

        public Task<IList<ContainerListResponse>> ListAsync(
            ContainersListParameters parameters,
            CancellationToken cancellationToken)
            => _inner.ListAsync(parameters, cancellationToken);

        public IAsyncEnumerable<string> StreamLogsAsync(
            string containerId,
            CancellationToken cancellationToken)
            => _inner.StreamLogsAsync(containerId, cancellationToken);

        public Task<string?> GetLogsAsync(string containerId, int tailLines, CancellationToken cancellationToken)
            => _inner.GetLogsAsync(containerId, tailLines, cancellationToken);

        public Task CreateVolumeAsync(
            VolumesCreateParameters parameters,
            CancellationToken cancellationToken)
            => _inner.CreateVolumeAsync(parameters, cancellationToken);

        public Task<string> ExecCaptureStdoutAsync(
            string containerId,
            IReadOnlyList<string> command,
            IReadOnlyList<string> environment,
            int? maxBytes,
            CancellationToken cancellationToken)
            => _inner.ExecCaptureStdoutAsync(containerId, command, environment, maxBytes, cancellationToken);

        public Task<string?> ReadFileAsync(string containerId, string filePath, CancellationToken cancellationToken)
            => _inner.ReadFileAsync(containerId, filePath, cancellationToken);

        public Task WriteFileAsync(
            string containerId,
            string filePath,
            string content,
            CancellationToken cancellationToken)
            => _inner.WriteFileAsync(containerId, filePath, content, cancellationToken);

        public Task ExecAsync(
            string containerId,
            IReadOnlyList<string> command,
            IReadOnlyList<string> environment,
            CancellationToken cancellationToken)
            => _inner.ExecAsync(containerId, command, environment, cancellationToken);
    }
}
