using Foundry.Modules.Issues.Domain;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.IssueKindTests;

public sealed class FromLabel
{
    [Fact]
    public void WhenLabelIsUnrecognized_ReturnsFeature()
    {
        // Arrange

        // Act
        IssueKind result = IssueKind.FromLabel("unknown");

        // Assert
        result.ShouldBe(IssueKind.Feature);
    }

    [Fact]
    public void WhenLabelIsFeature_ReturnsFeature()
    {
        // Arrange

        // Act
        IssueKind result = IssueKind.FromLabel("feature");

        // Assert
        result.ShouldBe(IssueKind.Feature);
    }

    [Fact]
    public void WhenLabelIsBug_ReturnsBug()
    {
        // Arrange

        // Act
        IssueKind result = IssueKind.FromLabel("bug");

        // Assert
        result.ShouldBe(IssueKind.Bug);
    }

    [Fact]
    public void WhenLabelIsRefactor_ReturnsRefactor()
    {
        // Arrange

        // Act
        IssueKind result = IssueKind.FromLabel("refactor");

        // Assert
        result.ShouldBe(IssueKind.Refactor);
    }

    [Fact]
    public void WhenLabelIsDocumentation_ReturnsDocumentation()
    {
        // Arrange

        // Act
        IssueKind result = IssueKind.FromLabel("documentation");

        // Assert
        result.ShouldBe(IssueKind.Documentation);
    }

    [Fact]
    public void WhenLabelIsUpperCase_MatchesCaseInsensitively()
    {
        // Arrange

        // Act
        IssueKind result = IssueKind.FromLabel("BUG");

        // Assert
        result.ShouldBe(IssueKind.Bug);
    }
}
