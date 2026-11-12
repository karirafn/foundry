using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Services;
using Foundry.Modules.Issues.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Services.DependencyCycleDetectorTests;

public sealed class FindCycleClosedBy
{
    private const int Issue = 42;
    private const int Blocker = 15;

    [Fact]
    public void WhenBlockerReachesIssueThroughAnotherIssue_ReturnsTheClosedPath()
    {
        // Arrange
        IReadOnlyList<DependencyEdge> edges =
        [
            new DependencyEdge(IssueNumber: 15, BlockedByIssueNumber: 31),
            new DependencyEdge(IssueNumber: 31, BlockedByIssueNumber: 42),
        ];

        // Act
        DependencyCycle? cycle = DependencyCycleDetector.FindCycleClosedBy(edges, Issue, Blocker);

        // Assert
        cycle.ShouldNotBeNull().Path.ShouldBe([42, 15, 31, 42]);
    }

    [Fact]
    public void WhenBlockerCannotReachIssue_ReturnsNull()
    {
        // Arrange
        IReadOnlyList<DependencyEdge> edges =
        [
            new DependencyEdge(IssueNumber: 15, BlockedByIssueNumber: 31),
        ];

        // Act
        DependencyCycle? cycle = DependencyCycleDetector.FindCycleClosedBy(edges, Issue, Blocker);

        // Assert
        cycle.ShouldBeNull();
    }

    [Fact]
    public void WhenACycleExistsElsewhere_ReturnsNull()
    {
        // Arrange
        IReadOnlyList<DependencyEdge> edges =
        [
            new DependencyEdge(IssueNumber: 7, BlockedByIssueNumber: 8),
            new DependencyEdge(IssueNumber: 8, BlockedByIssueNumber: 7),
        ];

        // Act
        DependencyCycle? cycle = DependencyCycleDetector.FindCycleClosedBy(edges, Issue, Blocker);

        // Assert
        cycle.ShouldBeNull();
    }

    [Fact]
    public void WhenACycleThroughTheIssueDoesNotUseTheBlocker_ReturnsNull()
    {
        // Arrange
        IReadOnlyList<DependencyEdge> edges =
        [
            new DependencyEdge(IssueNumber: 42, BlockedByIssueNumber: 31),
            new DependencyEdge(IssueNumber: 31, BlockedByIssueNumber: 42),
        ];

        // Act
        DependencyCycle? cycle = DependencyCycleDetector.FindCycleClosedBy(edges, Issue, Blocker);

        // Assert
        cycle.ShouldBeNull();
    }

    [Fact]
    public void WhenSeveralPathsReachTheIssue_ReturnsTheShortest()
    {
        // Arrange
        IReadOnlyList<DependencyEdge> edges =
        [
            new DependencyEdge(IssueNumber: 15, BlockedByIssueNumber: 33),
            new DependencyEdge(IssueNumber: 33, BlockedByIssueNumber: 42),
            new DependencyEdge(IssueNumber: 15, BlockedByIssueNumber: 31),
            new DependencyEdge(IssueNumber: 31, BlockedByIssueNumber: 32),
            new DependencyEdge(IssueNumber: 32, BlockedByIssueNumber: 42),
        ];

        // Act
        DependencyCycle? cycle = DependencyCycleDetector.FindCycleClosedBy(edges, Issue, Blocker);

        // Assert
        cycle.ShouldNotBeNull().Path.ShouldBe([42, 15, 33, 42]);
    }

    [Fact]
    public void WhenTheBlockerIsAlreadyBlockedByTheIssue_ReturnsThePathOfThreeEntries()
    {
        // Arrange
        IReadOnlyList<DependencyEdge> edges =
        [
            new DependencyEdge(IssueNumber: 15, BlockedByIssueNumber: 42),
        ];

        // Act
        DependencyCycle? cycle = DependencyCycleDetector.FindCycleClosedBy(edges, Issue, Blocker);

        // Assert
        cycle.ShouldNotBeNull().Path.ShouldBe([42, 15, 42]);
    }

    [Fact]
    public void WhenTheIssueIsItsOwnBlocker_ReturnsNull()
    {
        // Arrange
        IReadOnlyList<DependencyEdge> edges = [];

        // Act
        DependencyCycle? cycle = DependencyCycleDetector.FindCycleClosedBy(edges, Issue, Issue);

        // Assert
        cycle.ShouldBeNull();
    }
}
