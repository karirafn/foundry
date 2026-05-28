using Foundry.WebApi.Modules.Issues;
using Foundry.WebApi.Modules.Issues.Domain;

using Shouldly;

using Xunit;

namespace Foundry.WebApi.UnitTests.Modules.Issues.Domain.DependencyCycleDetectorTests;

public sealed class DetectCycles
{
    [Fact]
    public void WhenGraphIsEmpty_ReturnsNoCycles()
    {
        // Arrange
        IReadOnlyList<DependencyEdge> edges = [];

        // Act
        IReadOnlyList<IReadOnlyList<int>> cycles = DependencyCycleDetector.DetectCycles(edges);

        // Assert
        cycles.ShouldBeEmpty();
    }

    [Fact]
    public void WhenLinearChain_ReturnsNoCycles()
    {
        // Arrange
        // A (1) is blocked by B (2), B (2) is blocked by C (3): 1→2→3
        IReadOnlyList<DependencyEdge> edges =
        [
            new DependencyEdge(IssueNumber: 1, BlockedByIssueNumber: 2),
            new DependencyEdge(IssueNumber: 2, BlockedByIssueNumber: 3),
        ];

        // Act
        IReadOnlyList<IReadOnlyList<int>> cycles = DependencyCycleDetector.DetectCycles(edges);

        // Assert
        cycles.ShouldBeEmpty();
    }

    [Fact]
    public void WhenSimpleCycle_ReturnsCyclePath()
    {
        // Arrange
        // A (1) is blocked by B (2), B (2) is blocked by A (1): 1→2→1
        IReadOnlyList<DependencyEdge> edges =
        [
            new DependencyEdge(IssueNumber: 1, BlockedByIssueNumber: 2),
            new DependencyEdge(IssueNumber: 2, BlockedByIssueNumber: 1),
        ];

        // Act
        IReadOnlyList<IReadOnlyList<int>> cycles = DependencyCycleDetector.DetectCycles(edges);

        // Assert
        cycles.Count.ShouldBe(1);
        cycles[0].ShouldContain(1);
        cycles[0].ShouldContain(2);
    }

    [Fact]
    public void WhenThreeNodeCycle_ReturnsCyclePath()
    {
        // Arrange
        // A (1) blocked by B (2), B (2) blocked by C (3), C (3) blocked by A (1): 1→2→3→1
        IReadOnlyList<DependencyEdge> edges =
        [
            new DependencyEdge(IssueNumber: 1, BlockedByIssueNumber: 2),
            new DependencyEdge(IssueNumber: 2, BlockedByIssueNumber: 3),
            new DependencyEdge(IssueNumber: 3, BlockedByIssueNumber: 1),
        ];

        // Act
        IReadOnlyList<IReadOnlyList<int>> cycles = DependencyCycleDetector.DetectCycles(edges);

        // Assert
        cycles.Count.ShouldBe(1);
        cycles[0].ShouldContain(1);
        cycles[0].ShouldContain(2);
        cycles[0].ShouldContain(3);
    }

    [Fact]
    public void WhenMixedGraphWithOneCycle_ReturnsOnlyTheCycle()
    {
        // Arrange
        // Linear chain: 10→11→12 (no cycle)
        // Cycle: 1→2→3→1
        IReadOnlyList<DependencyEdge> edges =
        [
            new DependencyEdge(IssueNumber: 10, BlockedByIssueNumber: 11),
            new DependencyEdge(IssueNumber: 11, BlockedByIssueNumber: 12),
            new DependencyEdge(IssueNumber: 1, BlockedByIssueNumber: 2),
            new DependencyEdge(IssueNumber: 2, BlockedByIssueNumber: 3),
            new DependencyEdge(IssueNumber: 3, BlockedByIssueNumber: 1),
        ];

        // Act
        IReadOnlyList<IReadOnlyList<int>> cycles = DependencyCycleDetector.DetectCycles(edges);

        // Assert
        cycles.Count.ShouldBe(1);
        IReadOnlyList<int> cycle = cycles[0];
        cycle.ShouldSatisfyAllConditions(
            () => cycle.ShouldContain(1),
            () => cycle.ShouldContain(2),
            () => cycle.ShouldContain(3),
            () => cycle.ShouldNotContain(10),
            () => cycle.ShouldNotContain(11),
            () => cycle.ShouldNotContain(12));
    }
}
