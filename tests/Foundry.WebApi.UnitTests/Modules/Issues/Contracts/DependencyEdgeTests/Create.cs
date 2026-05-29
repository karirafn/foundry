using Foundry.Modules.Issues.Contracts;

using Shouldly;

using Xunit;

namespace Foundry.WebApi.UnitTests.Modules.Issues.Contracts.DependencyEdgeTests;

public sealed class Create
{
    [Fact]
    public void WhenCreated_HoldsProvidedValues()
    {
        // Arrange

        // Act
        DependencyEdge edge = new(IssueNumber: 42, BlockedByIssueNumber: 10);

        // Assert
        edge.ShouldSatisfyAllConditions(
            () => edge.IssueNumber.ShouldBe(42),
            () => edge.BlockedByIssueNumber.ShouldBe(10));
    }
}
