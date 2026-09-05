using Foundry.Modules.Monitoring.Contracts;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Contracts.ReviewCommentTests;

public sealed class Create
{
    [Fact]
    public void WhenCreatedWithAllValues_HoldsProvidedValues()
    {
        // Arrange

        // Act
        ReviewComment comment = new("Please extract this method", FilePath: "src/Foo.cs", Line: 42);

        // Assert
        comment.ShouldSatisfyAllConditions(
            () => comment.FilePath.ShouldBe("src/Foo.cs"),
            () => comment.Line.ShouldBe(42));
    }

    [Fact]
    public void WhenCreatedWithoutFileLocation_FilePathAndLineAreNull()
    {
        // Arrange

        // Act
        ReviewComment comment = new("General feedback");

        // Assert
        comment.ShouldSatisfyAllConditions(
            () => comment.FilePath.ShouldBeNull(),
            () => comment.Line.ShouldBeNull());
    }
}
