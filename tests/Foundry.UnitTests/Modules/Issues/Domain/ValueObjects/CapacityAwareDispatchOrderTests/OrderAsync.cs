using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.ValueObjects.CapacityAwareDispatchOrderTests;

/// <summary>
/// Tests for <see cref="CapacityAwareDispatchOrder.Order"/>.
/// All tests use in-memory issue objects — no database.
/// </summary>
public sealed class OrderAsync
{
    private static readonly MonitoredRepositoryId RepoA = MonitoredRepositoryId.New();
    private static readonly MonitoredRepositoryId RepoB = MonitoredRepositoryId.New();

    private static readonly DateTimeOffset BaseTime = new(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static FreshQueuedIssue MakeIssue(
        MonitoredRepositoryId repoId,
        int issueNumber,
        DateTimeOffset? detectedAt = null) =>
        new IssueBuilder()
            .WithMonitoredRepositoryId(repoId)
            .WithIssueNumber(issueNumber)
            .WithTitle($"Issue {issueNumber}")
            .WithDetectedAt(detectedAt ?? BaseTime)
            .FreshQueued();

    private static Dictionary<MonitoredRepositoryId, int> HeadroomOf(
        params (MonitoredRepositoryId Id, int Headroom)[] entries) =>
        entries.ToDictionary(e => e.Id, e => e.Headroom);

    private static int PositionOf(MonitoredRepositoryId repoId, IReadOnlyList<EligibleRepository> eligibleRepos)
    {
        EligibleRepository? repo = eligibleRepos.FirstOrDefault(r => r.Id == repoId.Value);
        return repo?.Position ?? 0;
    }

    // Cycle 1: single repo, single issue, has headroom — issue is placed first
    [Fact]
    public void WhenSingleIssueWithHeadroom_PlacesItFirst()
    {
        // Arrange
        FreshQueuedIssue issue = MakeIssue(RepoA, issueNumber: 1);
        IReadOnlyList<EligibleRepository> eligibleRepos =
        [
            new EligibleRepository(RepoA.Value, Position: 0, MaxConcurrentWorkers: 1),
        ];
        List<(QueuedIssue Issue, DispatchOrderKey Key)> keyedIssues =
        [
            (issue, DispatchOrderKey.For(issue, PositionOf(RepoA, eligibleRepos))),
        ];
        Dictionary<MonitoredRepositoryId, int> headroom = HeadroomOf((RepoA, 1));

        // Act
        IReadOnlyList<QueuedIssue> ordered = CapacityAwareDispatchOrder.Order(keyedIssues, headroom);

        // Assert
        ordered.Count.ShouldBe(1);
        ordered[0].Id.ShouldBe(issue.Id);
    }

    // Cycle 2: single repo, single issue, saturated — issue is excluded (headroom = 0)
    [Fact]
    public void WhenSingleIssueWithNoHeadroom_ReturnsEmpty()
    {
        // Arrange
        FreshQueuedIssue issue = MakeIssue(RepoA, issueNumber: 1);
        IReadOnlyList<EligibleRepository> eligibleRepos =
        [
            new EligibleRepository(RepoA.Value, Position: 0, MaxConcurrentWorkers: 1),
        ];
        List<(QueuedIssue Issue, DispatchOrderKey Key)> keyedIssues =
        [
            (issue, DispatchOrderKey.For(issue, PositionOf(RepoA, eligibleRepos))),
        ];
        Dictionary<MonitoredRepositoryId, int> headroom = HeadroomOf((RepoA, 0));

        // Act
        IReadOnlyList<QueuedIssue> ordered = CapacityAwareDispatchOrder.Order(keyedIssues, headroom);

        // Assert
        ordered.ShouldBeEmpty();
    }

    // Cycle 3: two repos each with one issue and headroom 1 — dispatch order determines sequence
    [Fact]
    public void WhenTwoReposEachWithOneIssue_PlacesInDispatchOrderKeyOrder()
    {
        // Arrange — RepoA at position 0 (higher priority than RepoB at position 1)
        FreshQueuedIssue issueA = MakeIssue(RepoA, issueNumber: 1, detectedAt: BaseTime);
        FreshQueuedIssue issueB = MakeIssue(RepoB, issueNumber: 2, detectedAt: BaseTime);

        IReadOnlyList<EligibleRepository> eligibleRepos =
        [
            new EligibleRepository(RepoA.Value, Position: 0, MaxConcurrentWorkers: 1),
            new EligibleRepository(RepoB.Value, Position: 1, MaxConcurrentWorkers: 1),
        ];

        List<(QueuedIssue Issue, DispatchOrderKey Key)> keyedIssues =
        [
            (issueA, DispatchOrderKey.For(issueA, 0)),
            (issueB, DispatchOrderKey.For(issueB, 1)),
        ];
        Dictionary<MonitoredRepositoryId, int> headroom = HeadroomOf((RepoA, 1), (RepoB, 1));

        // Act
        IReadOnlyList<QueuedIssue> ordered = CapacityAwareDispatchOrder.Order(keyedIssues, headroom);

        // Assert
        ordered.Count.ShouldBe(2);
        ordered[0].Id.ShouldBe(issueA.Id);
        ordered[1].Id.ShouldBe(issueB.Id);
    }

    // Cycle 4: two repos, first is saturated — second repo's issue heads the result
    [Fact]
    public void WhenHigherPriorityRepoIsSaturated_LowerPriorityRepoHeadsResult()
    {
        // Arrange — RepoA at position 0 but saturated; RepoB at position 1 with headroom
        FreshQueuedIssue issueA = MakeIssue(RepoA, issueNumber: 1, detectedAt: BaseTime);
        FreshQueuedIssue issueB = MakeIssue(RepoB, issueNumber: 2, detectedAt: BaseTime);

        List<(QueuedIssue Issue, DispatchOrderKey Key)> keyedIssues =
        [
            (issueA, DispatchOrderKey.For(issueA, 0)),
            (issueB, DispatchOrderKey.For(issueB, 1)),
        ];
        Dictionary<MonitoredRepositoryId, int> headroom = HeadroomOf((RepoA, 0), (RepoB, 1));

        // Act
        IReadOnlyList<QueuedIssue> ordered = CapacityAwareDispatchOrder.Order(keyedIssues, headroom);

        // Assert
        ordered.Count.ShouldBe(1);
        ordered[0].Id.ShouldBe(issueB.Id);
    }

    // Cycle 5 (THE CRITICAL CASE — ADR 0074 rejected-alternative hazard):
    // Repo R with MaxConcurrentWorkers=2, two issues interleaved against competing RepoB.
    // A sort-key shortcut would see each issue independently and never give R a second slot
    // at limits ≥ 2. The multi-pass walk must place both R issues at positions 1 and 2
    // when R has headroom ≥ 2 and no other competing repo.
    [Fact]
    public void WhenRepoHasHeadroomTwo_AndTwoIssues_PlacesBothConsecutively()
    {
        // Arrange — RepoA has 2 issues and headroom 2; RepoB has 1 issue and headroom 1.
        // Both repos are at position 0 (same tier, same position, different time).
        // RepoA's issues detected earlier → lower DispatchOrderKey → head the raw order.
        FreshQueuedIssue issueA1 = MakeIssue(RepoA, issueNumber: 1, detectedAt: BaseTime);
        FreshQueuedIssue issueA2 = MakeIssue(RepoA, issueNumber: 2, detectedAt: BaseTime.AddSeconds(1));
        FreshQueuedIssue issueB = MakeIssue(RepoB, issueNumber: 3, detectedAt: BaseTime.AddSeconds(2));

        List<(QueuedIssue Issue, DispatchOrderKey Key)> keyedIssues =
        [
            (issueA1, DispatchOrderKey.For(issueA1, 0)),
            (issueA2, DispatchOrderKey.For(issueA2, 0)),
            (issueB, DispatchOrderKey.For(issueB, 0)),
        ];
        Dictionary<MonitoredRepositoryId, int> headroom = HeadroomOf((RepoA, 2), (RepoB, 1));

        // Act
        IReadOnlyList<QueuedIssue> ordered = CapacityAwareDispatchOrder.Order(keyedIssues, headroom);

        // Assert — A's two issues come first (both within headroom), then B
        ordered.Count.ShouldBe(3);
        ordered[0].Id.ShouldBe(issueA1.Id);
        ordered[1].Id.ShouldBe(issueA2.Id);
        ordered[2].Id.ShouldBe(issueB.Id);
    }

    // Cycle 6: interleaved scenario — RepoA (higher priority) and RepoB each have 2 issues,
    // both at headroom 1. With headroom=1 each repo can dispatch 1 issue total. The walk
    // places A1 first (A at position 0, earlier key), then B1 — A2 and B2 are deferred and
    // remain deferred because both repos are at zero headroom after pass 1.
    [Fact]
    public void WhenTwoReposEachWithTwoIssuesAndHeadroomOne_PlacesOnePerRepo()
    {
        // Arrange — A at position 0, B at position 1; both headroom 1
        FreshQueuedIssue issueA1 = MakeIssue(RepoA, issueNumber: 1, detectedAt: BaseTime);
        FreshQueuedIssue issueA2 = MakeIssue(RepoA, issueNumber: 2, detectedAt: BaseTime.AddSeconds(1));
        FreshQueuedIssue issueB1 = MakeIssue(RepoB, issueNumber: 3, detectedAt: BaseTime);
        FreshQueuedIssue issueB2 = MakeIssue(RepoB, issueNumber: 4, detectedAt: BaseTime.AddSeconds(1));

        List<(QueuedIssue Issue, DispatchOrderKey Key)> keyedIssues =
        [
            (issueA1, DispatchOrderKey.For(issueA1, 0)),
            (issueA2, DispatchOrderKey.For(issueA2, 0)),
            (issueB1, DispatchOrderKey.For(issueB1, 1)),
            (issueB2, DispatchOrderKey.For(issueB2, 1)),
        ];
        Dictionary<MonitoredRepositoryId, int> headroom = HeadroomOf((RepoA, 1), (RepoB, 1));

        // Act
        IReadOnlyList<QueuedIssue> ordered = CapacityAwareDispatchOrder.Order(keyedIssues, headroom);

        // Assert — pass 1: A1 placed (A headroom → 0), B1 placed (B headroom → 0)
        //          pass 2: A2 and B2 deferred (both repos saturated) → no progress → stop
        ordered.Count.ShouldBe(2);
        ordered[0].Id.ShouldBe(issueA1.Id);
        ordered[1].Id.ShouldBe(issueB1.Id);
    }

    // Cycle 7: empty input — returns empty
    [Fact]
    public void WhenEmptyInput_ReturnsEmpty()
    {
        // Arrange
        List<(QueuedIssue Issue, DispatchOrderKey Key)> keyedIssues = [];
        Dictionary<MonitoredRepositoryId, int> headroom = [];

        // Act
        IReadOnlyList<QueuedIssue> ordered = CapacityAwareDispatchOrder.Order(keyedIssues, headroom);

        // Assert
        ordered.ShouldBeEmpty();
    }
}
