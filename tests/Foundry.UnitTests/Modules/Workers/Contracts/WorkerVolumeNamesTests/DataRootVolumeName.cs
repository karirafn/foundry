using Foundry.Modules.Workers.Contracts;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Contracts.WorkerVolumeNamesTests;

public sealed class DataRootVolumeName
{
    [Fact]
    public void WhenGivenWorkerRunId_ReturnsNameWithPrefixAndRunIdValue()
    {
        // Arrange
        Guid guid = new("a1b2c3d4-e5f6-7890-abcd-ef1234567890");
        WorkerRunId runId = WorkerRunId.From(guid);

        // Act
        string volumeName = WorkerVolumeNames.DataRootVolumeName(runId);

        // Assert
        volumeName.ShouldBe($"foundry-worker-docker-{guid}");
    }

    [Fact]
    public void WhenGivenWorkerRunId_StartsWithPrefix()
    {
        // Arrange
        WorkerRunId runId = WorkerRunId.New();

        // Act
        string volumeName = WorkerVolumeNames.DataRootVolumeName(runId);

        // Assert
        volumeName.ShouldStartWith(WorkerVolumeNames.DataRootVolumeNamePrefix);
    }
}
