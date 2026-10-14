using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Contracts;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.ValueObjects.DispatchHeadroomTests;

/// <summary>
/// Tests for <see cref="DispatchHeadroom.ForForecast"/>.
/// </summary>
public sealed class ForForecast
{
    private static readonly MonitoredRepositoryId RepoA = MonitoredRepositoryId.New();
    private static readonly MonitoredRepositoryId RepoB = MonitoredRepositoryId.New();
    private static readonly MonitoredRepositoryId RepoC = MonitoredRepositoryId.New();

    private static EligibleRepository Repo(MonitoredRepositoryId id, int maxConcurrentWorkers, int position = 0) =>
        new(id.Value, position, maxConcurrentWorkers);

    // Cycle 1a (Tracer): Under saturation, in-flight repo gets max(0, Max - (inFlight - 1)).
    [Fact]
    public void WhenGloballySaturated_InFlightRepoGetsAdjustedHeadroom()
    {
        // Arrange — global max 1, repo A has 1 in-flight worker (total = global max → saturated)
        IReadOnlyList<EligibleRepository> repos = [Repo(RepoA, maxConcurrentWorkers: 1)];
        IReadOnlyDictionary<MonitoredRepositoryId, int> inFlight =
            new Dictionary<MonitoredRepositoryId, int> { [RepoA] = 1 };

        // Act
        DispatchHeadroom headroom = DispatchHeadroom.ForForecast(repos, inFlight, globalMax: 1);

        // Assert — max(0, 1 - (1 - 1)) = max(0, 1) = 1
        headroom.For(RepoA).ShouldBe(1);
    }

    // Cycle 1a (part 2): Under saturation, idle repo keeps full limit.
    [Fact]
    public void WhenGloballySaturated_IdleRepoKeepsFullLimit()
    {
        // Arrange — global max 1, repo A has 1 in-flight; repo B is idle
        IReadOnlyList<EligibleRepository> repos =
        [
            Repo(RepoA, maxConcurrentWorkers: 1),
            Repo(RepoB, maxConcurrentWorkers: 2),
        ];
        IReadOnlyDictionary<MonitoredRepositoryId, int> inFlight =
            new Dictionary<MonitoredRepositoryId, int> { [RepoA] = 1 };

        // Act
        DispatchHeadroom headroom = DispatchHeadroom.ForForecast(repos, inFlight, globalMax: 1);

        // Assert — idle repo B keeps its full limit
        headroom.For(RepoB).ShouldBe(2);
    }

    // Cycle 1c: Below saturation, ForForecast equals ForClaim for every repo.
    [Fact]
    public void WhenBelowSaturation_EqualsForClaim()
    {
        // Arrange — global max 2, only 1 in-flight (total < globalMax)
        IReadOnlyList<EligibleRepository> repos =
        [
            Repo(RepoA, maxConcurrentWorkers: 2),
            Repo(RepoB, maxConcurrentWorkers: 1),
        ];
        IReadOnlyDictionary<MonitoredRepositoryId, int> inFlight =
            new Dictionary<MonitoredRepositoryId, int> { [RepoA] = 1 };

        DispatchHeadroom claim = DispatchHeadroom.ForClaim(repos, inFlight);

        // Act
        DispatchHeadroom forecast = DispatchHeadroom.ForForecast(repos, inFlight, globalMax: 2);

        // Assert — below saturation, forecast == claim for every repo
        forecast.For(RepoA).ShouldBe(claim.For(RepoA));
        forecast.For(RepoB).ShouldBe(claim.For(RepoB));
    }

    // Cycle 1d: Floor at 0 — repo limit lowered below in-flight count (limit 1, in-flight 2 → 0).
    [Fact]
    public void WhenRepoLimitLowerThanInFlight_HeadroomIsZero()
    {
        // Arrange — global max 1, repo A has limit 1 but 2 in-flight
        IReadOnlyList<EligibleRepository> repos = [Repo(RepoA, maxConcurrentWorkers: 1)];
        IReadOnlyDictionary<MonitoredRepositoryId, int> inFlight =
            new Dictionary<MonitoredRepositoryId, int> { [RepoA] = 2 };

        // Act — total(2) >= globalMax(1) → saturated; max(0, 1 - (2 - 1)) = max(0, 0) = 0
        DispatchHeadroom headroom = DispatchHeadroom.ForForecast(repos, inFlight, globalMax: 1);

        // Assert
        headroom.For(RepoA).ShouldBe(0);
    }

    // AC: repo limit 2 with 2 in-flight under saturation → forecast headroom 1.
    [Fact]
    public void WhenRepoLimitTwoWithTwoInFlight_UnderSaturation_HeadroomIsOne()
    {
        // Arrange — global max 2, repo A has limit 2 and 2 in-flight (total=2 >= globalMax=2)
        IReadOnlyList<EligibleRepository> repos = [Repo(RepoA, maxConcurrentWorkers: 2)];
        IReadOnlyDictionary<MonitoredRepositoryId, int> inFlight =
            new Dictionary<MonitoredRepositoryId, int> { [RepoA] = 2 };

        // Act — max(0, 2 - (2 - 1)) = max(0, 1) = 1
        DispatchHeadroom headroom = DispatchHeadroom.ForForecast(repos, inFlight, globalMax: 2);

        // Assert
        headroom.For(RepoA).ShouldBe(1);
    }

    // Absent repo returns 0 even under saturation.
    [Fact]
    public void WhenRepoNotInEligibleList_ReturnsZero()
    {
        // Arrange
        IReadOnlyList<EligibleRepository> repos = [Repo(RepoA, maxConcurrentWorkers: 1)];
        IReadOnlyDictionary<MonitoredRepositoryId, int> inFlight =
            new Dictionary<MonitoredRepositoryId, int> { [RepoA] = 1 };

        // Act
        DispatchHeadroom headroom = DispatchHeadroom.ForForecast(repos, inFlight, globalMax: 1);

        // Assert — unknown repo C is absent → 0
        headroom.For(RepoC).ShouldBe(0);
    }

    // When total in-flight == 0, ForForecast below saturation equals ForClaim (no adjustment).
    [Fact]
    public void WhenNoInFlightWorkers_EqualForClaim()
    {
        // Arrange — global max 1, no in-flight workers
        IReadOnlyList<EligibleRepository> repos =
        [
            Repo(RepoA, maxConcurrentWorkers: 2),
            Repo(RepoB, maxConcurrentWorkers: 3),
        ];
        IReadOnlyDictionary<MonitoredRepositoryId, int> inFlight =
            new Dictionary<MonitoredRepositoryId, int>();

        // Act
        DispatchHeadroom forecast = DispatchHeadroom.ForForecast(repos, inFlight, globalMax: 1);
        DispatchHeadroom claim = DispatchHeadroom.ForClaim(repos, inFlight);

        // Assert
        forecast.For(RepoA).ShouldBe(claim.For(RepoA));
        forecast.For(RepoB).ShouldBe(claim.For(RepoB));
    }
}
