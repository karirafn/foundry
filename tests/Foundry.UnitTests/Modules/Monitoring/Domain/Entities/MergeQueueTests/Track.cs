using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.Entities.MergeQueueTests;

public sealed class Track
{
    private static readonly MonitoredRepositoryId RepositoryId = MonitoredRepositoryId.New();
    private static readonly DateTimeOffset Episode = new(2026, 11, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 11, 0, 0, TimeSpan.Zero);

    private static MergePendingIssueInfo Issue(int number, DateTimeOffset enteredAt) =>
        new(number, $"https://github.com/octocat/hello-world/pull/{number}", $"feat/{number}-thing", "abc123", enteredAt, enteredAt);

    [Fact]
    public void FirstSighting_StartsANewRowForTheEpisode()
    {
        // Arrange
        MergeQueue queue = MergeQueue.From(RepositoryId, []);

        // Act
        TrackedMerge tracked = queue.Track(Issue(7, Episode));

        // Assert
        tracked.ShouldBeOfType<TrackedMerge.Started>();
        tracked.Merge.MonitoredRepositoryId.ShouldBe(RepositoryId);
        tracked.Merge.IssueNumber.ShouldBe(7);
        tracked.Merge.EnteredMergePendingAt.ShouldBe(Episode);
        tracked.Merge.TransientFailures.ShouldBe(0);
    }

    [Fact]
    public void SecondSightingInTheSamePass_ReturnsTheRowStartedByTheFirst()
    {
        // Arrange
        MergeQueue queue = MergeQueue.From(RepositoryId, []);
        PendingMerge first = queue.Track(Issue(7, Episode)).Merge;

        // Act
        TrackedMerge tracked = queue.Track(Issue(7, Episode));

        // Assert
        tracked.ShouldBeOfType<TrackedMerge.Existing>();
        tracked.Merge.ShouldBeSameAs(first);
    }

    [Fact]
    public void SameEpisodeSighting_ReturnsTheExistingRowUntouched()
    {
        // Arrange
        PendingMerge row = PendingMerge.Start(RepositoryId, 7, Episode);
        row.RecordTransientFailure(Now);
        DateTimeOffset? backoff = row.NextAttemptAt;
        MergeQueue queue = MergeQueue.From(RepositoryId, [row]);

        // Act
        TrackedMerge tracked = queue.Track(Issue(7, Episode));

        // Assert
        tracked.ShouldBeOfType<TrackedMerge.Existing>();
        tracked.Merge.ShouldBeSameAs(row);
        tracked.Merge.TransientFailures.ShouldBe(1);
        tracked.Merge.NextAttemptAt.ShouldBe(backoff);
    }

    [Fact]
    public void NewEpisodeSighting_RestartsTheRow()
    {
        // Arrange
        PendingMerge row = PendingMerge.Start(RepositoryId, 7, Episode);
        row.RecordTransientFailure(Now);
        row.MarkResolved(Now);
        MergeQueue queue = MergeQueue.From(RepositoryId, [row]);
        DateTimeOffset newEpisode = Episode.AddDays(1);

        // Act
        TrackedMerge tracked = queue.Track(Issue(7, newEpisode));

        // Assert
        tracked.ShouldBeOfType<TrackedMerge.Existing>();
        tracked.Merge.ShouldBeSameAs(row);
        tracked.Merge.EnteredMergePendingAt.ShouldBe(newEpisode);
        tracked.Merge.TransientFailures.ShouldBe(0);
        tracked.Merge.NextAttemptAt.ShouldBeNull();
        tracked.Merge.IsResolved.ShouldBeFalse();
    }
}
