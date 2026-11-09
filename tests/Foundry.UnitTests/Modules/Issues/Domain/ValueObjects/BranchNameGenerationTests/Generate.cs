using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.ValueObjects.BranchNameGenerationTests;

public sealed class Generate
{
    [Fact]
    public void WhenUnclassified_FallsBackToFeaturePrefix()
    {
        // Arrange

        // Act
        BranchName result = BranchName.Generate(KindClassification.Unclassified, 42, "add retry");

        // Assert
        result.Value.ShouldBe("feat/42-add-retry");
    }

    [Fact]
    public void WhenLabelled_UsesLabelledKindPrefix()
    {
        // Arrange
        KindClassification classification = new KindClassification.Labelled(IssueKind.Bug);

        // Act
        BranchName result = BranchName.Generate(classification, 7, "null ref on login");

        // Assert
        result.Value.ShouldBe("fix/7-null-ref-on-login");
    }
}
