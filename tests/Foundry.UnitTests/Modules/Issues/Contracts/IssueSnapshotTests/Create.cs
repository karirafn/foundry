using Foundry.Modules.Issues.Contracts;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Contracts.IssueSnapshotTests;

public sealed class Create
{
    [Fact]
    public void WhenCreated_HoldsProvidedValues()
    {
        // Arrange

        // Act
        IssueSnapshot snapshot = new("Fix bug", ["bug"]);

        // Assert
        snapshot.ShouldSatisfyAllConditions(
            () => snapshot.Title.ShouldBe("Fix bug"),
            () => snapshot.Labels.Count.ShouldBe(1));
    }
}
