using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Contracts;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.ValueObjects.DispatchHeadroomTests;

/// <summary>
/// Tests for <see cref="DispatchHeadroom.ForClaim"/>.
/// </summary>
public sealed class ForClaim
{
    private static readonly MonitoredRepositoryId RepoA = MonitoredRepositoryId.New();
    private static readonly MonitoredRepositoryId RepoB = MonitoredRepositoryId.New();
    private static readonly MonitoredRepositoryId Unknown = MonitoredRepositoryId.New();

    private static EligibleRepository Repo(MonitoredRepositoryId id, int maxConcurrentWorkers, int position = 0) =>
        new(id.Value, position, maxConcurrentWorkers);

    // Cycle 1b: ForClaim gives max(0, MaxConcurrentWorkers - inFlight).
    [Fact]
    public void WhenInFlightBelowLimit_ReturnsRemainingSlots()
    {
        // Arrange — repo A has limit 3 and 1 in-flight worker
        IReadOnlyList<EligibleRepository> repos = [Repo(RepoA, maxConcurrentWorkers: 3)];
        IReadOnlyDictionary<MonitoredRepositoryId, int> inFlight =
            new Dictionary<MonitoredRepositoryId, int> { [RepoA] = 1 };

        // Act
        DispatchHeadroom headroom = DispatchHeadroom.ForClaim(repos, inFlight);

        // Assert — max(0, 3 - 1) = 2
        headroom.For(RepoA).ShouldBe(2);
    }

    // Cycle 1b: Repo absent from in-flight dict gets full MaxConcurrentWorkers.
    [Fact]
    public void WhenRepoAbsentFromInFlight_ReturnsFullLimit()
    {
        // Arrange — repo A has limit 2, no in-flight
        IReadOnlyList<EligibleRepository> repos = [Repo(RepoA, maxConcurrentWorkers: 2)];
        IReadOnlyDictionary<MonitoredRepositoryId, int> inFlight =
            new Dictionary<MonitoredRepositoryId, int>();

        // Act
        DispatchHeadroom headroom = DispatchHeadroom.ForClaim(repos, inFlight);

        // Assert
        headroom.For(RepoA).ShouldBe(2);
    }

    // Cycle 1b: Repo at capacity returns 0.
    [Fact]
    public void WhenRepoAtCapacity_ReturnsZero()
    {
        // Arrange — repo A has limit 1 and 1 in-flight (saturated)
        IReadOnlyList<EligibleRepository> repos = [Repo(RepoA, maxConcurrentWorkers: 1)];
        IReadOnlyDictionary<MonitoredRepositoryId, int> inFlight =
            new Dictionary<MonitoredRepositoryId, int> { [RepoA] = 1 };

        // Act
        DispatchHeadroom headroom = DispatchHeadroom.ForClaim(repos, inFlight);

        // Assert
        headroom.For(RepoA).ShouldBe(0);
    }

    // Cycle 1b: Repo limit lowered below in-flight is floored at 0.
    [Fact]
    public void WhenInFlightExceedsLimit_ReturnsZero()
    {
        // Arrange — repo A has limit 1 but 2 in-flight (limit was lowered)
        IReadOnlyList<EligibleRepository> repos = [Repo(RepoA, maxConcurrentWorkers: 1)];
        IReadOnlyDictionary<MonitoredRepositoryId, int> inFlight =
            new Dictionary<MonitoredRepositoryId, int> { [RepoA] = 2 };

        // Act
        DispatchHeadroom headroom = DispatchHeadroom.ForClaim(repos, inFlight);

        // Assert — max(0, 1 - 2) = 0
        headroom.For(RepoA).ShouldBe(0);
    }

    // For(unknownRepo) returns 0 for a repo not in the eligible list.
    [Fact]
    public void WhenRepoNotInEligibleList_For_ReturnsZero()
    {
        // Arrange
        IReadOnlyList<EligibleRepository> repos = [Repo(RepoA, maxConcurrentWorkers: 2)];
        IReadOnlyDictionary<MonitoredRepositoryId, int> inFlight =
            new Dictionary<MonitoredRepositoryId, int>();

        // Act
        DispatchHeadroom headroom = DispatchHeadroom.ForClaim(repos, inFlight);

        // Assert — Unknown repo is absent → 0
        headroom.For(Unknown).ShouldBe(0);
    }

    // Multiple repos each get correct headroom independently.
    [Fact]
    public void WhenMultipleRepos_EachGetsCorrectHeadroom()
    {
        // Arrange
        IReadOnlyList<EligibleRepository> repos =
        [
            Repo(RepoA, maxConcurrentWorkers: 3),
            Repo(RepoB, maxConcurrentWorkers: 1),
        ];
        IReadOnlyDictionary<MonitoredRepositoryId, int> inFlight =
            new Dictionary<MonitoredRepositoryId, int>
            {
                [RepoA] = 1,
                [RepoB] = 1,
            };

        // Act
        DispatchHeadroom headroom = DispatchHeadroom.ForClaim(repos, inFlight);

        // Assert — RepoA: max(0, 3 - 1) = 2; RepoB: max(0, 1 - 1) = 0
        headroom.ShouldSatisfyAllConditions(
            () => headroom.For(RepoA).ShouldBe(2),
            () => headroom.For(RepoB).ShouldBe(0));
    }
}
