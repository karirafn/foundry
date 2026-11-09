using Foundry.Modules.Issues.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.ValueObjects.KindClassificationTests;

public sealed class FromKindLabel
{
    [Fact]
    public void WhenKindLabelIsNull_ReturnsUnclassified()
    {
        // Arrange

        // Act
        KindClassification result = KindClassification.FromKindLabel(null);

        // Assert
        result.ShouldBe(KindClassification.Unclassified);
    }

    [Fact]
    public void WhenKindLabelIsRecognized_ReturnsLabelledKind()
    {
        // Arrange

        // Act
        KindClassification result = KindClassification.FromKindLabel("documentation");

        // Assert
        result.ShouldBe(new KindClassification.Labelled(IssueKind.Documentation));
    }

    [Fact]
    public void WhenKindLabelIsUnrecognized_ReturnsUnclassified()
    {
        // Arrange

        // Act
        KindClassification result = KindClassification.FromKindLabel("claude");

        // Assert
        result.ShouldBe(KindClassification.Unclassified);
    }
}
