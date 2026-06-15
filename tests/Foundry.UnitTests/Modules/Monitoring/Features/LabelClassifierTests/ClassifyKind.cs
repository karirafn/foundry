using Foundry.Modules.Monitoring.Features;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.LabelClassifierTests;

public sealed class ClassifyKind
{
    [Fact]
    public void WhenNoRecognizedLabels_ReturnsFeature()
    {
        // Arrange
        string[] labels = ["foundry", "some-other-label"];

        // Act
        string result = LabelClassifier.ClassifyKind(labels);

        // Assert
        result.ShouldBe("feature");
    }

    [Fact]
    public void WhenLabelsBug_ReturnsBug()
    {
        // Arrange
        string[] labels = ["bug"];

        // Act
        string result = LabelClassifier.ClassifyKind(labels);

        // Assert
        result.ShouldBe("bug");
    }

    [Fact]
    public void WhenLabelsFeature_ReturnsFeature()
    {
        // Arrange
        string[] labels = ["feature"];

        // Act
        string result = LabelClassifier.ClassifyKind(labels);

        // Assert
        result.ShouldBe("feature");
    }

    [Fact]
    public void WhenLabelsRefactor_ReturnsRefactor()
    {
        // Arrange
        string[] labels = ["refactor"];

        // Act
        string result = LabelClassifier.ClassifyKind(labels);

        // Assert
        result.ShouldBe("refactor");
    }

    [Fact]
    public void WhenLabelsDocumentation_ReturnsDocumentation()
    {
        // Arrange
        string[] labels = ["documentation"];

        // Act
        string result = LabelClassifier.ClassifyKind(labels);

        // Assert
        result.ShouldBe("documentation");
    }

    [Fact]
    public void WhenLabelsBugAndFeature_ReturnsBug()
    {
        // Arrange
        string[] labels = ["feature", "bug"];

        // Act
        string result = LabelClassifier.ClassifyKind(labels);

        // Assert
        result.ShouldBe("bug");
    }

    [Fact]
    public void WhenLabelsCaseInsensitive_ReturnsCorrectKind()
    {
        // Arrange
        string[] labels = ["BUG"];

        // Act
        string result = LabelClassifier.ClassifyKind(labels);

        // Assert
        result.ShouldBe("bug");
    }
}
