using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.Entities.MergeQueueTests;

public sealed class From
{
    private static readonly DateTimeOffset Episode = new(2026, 11, 1, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void HoldsTheRepositoryItWasBuiltFor()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();

        // Act
        MergeQueue queue = MergeQueue.From(repositoryId, [PendingMerge.Start(repositoryId, 1, Episode)]);

        // Assert
        queue.RepositoryId.ShouldBe(repositoryId);
    }

    [Fact]
    public void ARowOfAnotherRepository_IsRejected()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        PendingMerge foreign = PendingMerge.Start(MonitoredRepositoryId.New(), 1, Episode);

        // Act / Assert
        Should.Throw<ArgumentException>(() => MergeQueue.From(repositoryId, [foreign]));
    }

    [Fact]
    public void TwoRowsForOneIssue_AreRejected()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        PendingMerge first = PendingMerge.Start(repositoryId, 1, Episode);
        PendingMerge duplicate = PendingMerge.Start(repositoryId, 1, Episode);

        // Act / Assert
        Should.Throw<ArgumentException>(() => MergeQueue.From(repositoryId, [first, duplicate]));
    }
}
