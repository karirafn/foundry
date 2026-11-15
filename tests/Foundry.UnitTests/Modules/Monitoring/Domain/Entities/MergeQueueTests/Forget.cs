using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.Entities.MergeQueueTests;

public sealed class Forget
{
    private static readonly MonitoredRepositoryId RepositoryId = MonitoredRepositoryId.New();
    private static readonly DateTimeOffset Episode = new(2026, 11, 1, 10, 0, 0, TimeSpan.Zero);

    private static MergePendingIssueInfo Issue(int number) =>
        new(number, $"https://github.com/octocat/hello-world/pull/{number}", $"feat/{number}-thing", "abc123", Episode, Episode);

    [Fact]
    public void ReturnsOnlyRowsWhoseIssueIsNoLongerMergePending()
    {
        // Arrange
        PendingMerge kept = PendingMerge.Start(RepositoryId, 1, Episode);
        PendingMerge left = PendingMerge.Start(RepositoryId, 2, Episode);
        PendingMerge alsoLeft = PendingMerge.Start(RepositoryId, 3, Episode);
        MergeQueue queue = MergeQueue.From(RepositoryId, [kept, left, alsoLeft]);

        // Act
        IReadOnlyList<PendingMerge> forgotten = queue.Forget(MergeQueue.HeldIssueNumbers([Issue(1), Issue(4)], []));

        // Assert
        forgotten.ShouldBe([left, alsoLeft], ignoreOrder: true);
    }

    [Fact]
    public void WhenEveryIssueIsStillMergePending_ReturnsNothing()
    {
        // Arrange
        PendingMerge row = PendingMerge.Start(RepositoryId, 1, Episode);
        MergeQueue queue = MergeQueue.From(RepositoryId, [row]);

        // Act
        IReadOnlyList<PendingMerge> forgotten = queue.Forget(MergeQueue.HeldIssueNumbers([Issue(1)], []));

        // Assert
        forgotten.ShouldBeEmpty();
    }

    [Fact]
    public void AForgottenIssueSightedAgain_StartsAFreshRow()
    {
        // Arrange
        PendingMerge row = PendingMerge.Start(RepositoryId, 1, Episode);
        MergeQueue queue = MergeQueue.From(RepositoryId, [row]);
        queue.Forget(MergeQueue.HeldIssueNumbers([], []));

        // Act
        TrackedMerge tracked = queue.Track(Issue(1));

        // Assert
        tracked.ShouldBeOfType<TrackedMerge.Started>();
        tracked.Merge.ShouldNotBeSameAs(row);
    }
}
