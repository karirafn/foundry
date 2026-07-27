using Foundry.Modules.Issues.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.ValueObjects.IssueKindTests;

public sealed class BranchPrefix
{
    [Fact]
    public void WhenKindIsFeature_ReturnsFeat()
    {
        // Arrange

        // Act
        string prefix = IssueKind.Feature.BranchPrefix;

        // Assert
        prefix.ShouldBe("feat");
    }

    [Fact]
    public void WhenKindIsBug_ReturnsFix()
    {
        // Arrange

        // Act
        string prefix = IssueKind.Bug.BranchPrefix;

        // Assert
        prefix.ShouldBe("fix");
    }

    [Fact]
    public void WhenKindIsRefactor_ReturnsRefactor()
    {
        // Arrange

        // Act
        string prefix = IssueKind.Refactor.BranchPrefix;

        // Assert
        prefix.ShouldBe("refactor");
    }

    [Fact]
    public void WhenKindIsDocumentation_ReturnsDocs()
    {
        // Arrange

        // Act
        string prefix = IssueKind.Documentation.BranchPrefix;

        // Assert
        prefix.ShouldBe("docs");
    }
}
