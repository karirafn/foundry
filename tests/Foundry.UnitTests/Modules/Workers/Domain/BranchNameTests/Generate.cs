using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Domain.BranchNameTests;

public sealed class Generate
{
    [Fact]
    public void WhenGivenPrefixNumberAndTitle_ProducesExpectedFormat()
    {
        // Arrange

        // Act
        BranchName result = BranchName.Generate("feat", 42, "add health check");

        // Assert
        result.Value.ShouldBe("feat/42-add-health-check");
    }

    [Fact]
    public void WhenTitleHasUpperCase_SlugifiesToLowerKebabCase()
    {
        // Arrange

        // Act
        BranchName result = BranchName.Generate("feat", 42, "Add Health Check Endpoint");

        // Assert
        result.Value.ShouldBe("feat/42-add-health-check-endpoint");
    }

    [Fact]
    public void WhenTitleHasSpecialCharacters_ReplacesWithHyphens()
    {
        // Arrange

        // Act
        BranchName result = BranchName.Generate("fix", 7, "fix: null ref on login!");

        // Assert
        result.Value.ShouldBe("fix/7-fix-null-ref-on-login");
    }

    [Fact]
    public void WhenTitleProducesConsecutiveHyphens_CollapsesToSingle()
    {
        // Arrange

        // Act
        BranchName result = BranchName.Generate("feat", 1, "add  double  space");

        // Assert
        result.Value.ShouldBe("feat/1-add-double-space");
    }

    [Fact]
    public void WhenSlugHasLeadingOrTrailingHyphens_TrimsThemFromSlug()
    {
        // Arrange

        // Act
        BranchName result = BranchName.Generate("feat", 5, "!leading and trailing!");

        // Assert
        result.Value.ShouldBe("feat/5-leading-and-trailing");
    }

    [Fact]
    public void WhenTitleHasNonAsciiCharacters_RemovesThem()
    {
        // Arrange

        // Act
        BranchName result = BranchName.Generate("feat", 3, "café order");

        // Assert
        result.Value.ShouldBe("feat/3-caf-order");
    }

    [Fact]
    public void WhenBranchNameWouldExceed100Chars_TruncatesSlug()
    {
        // Arrange
        string longTitle = new string('a', 200);

        // Act
        BranchName result = BranchName.Generate("feat", 1, longTitle);

        // Assert
        result.Value.Length.ShouldBeLessThanOrEqualTo(100);
    }

    [Fact]
    public void WhenTitleIsEmpty_ProducesPrefixAndNumberOnly()
    {
        // Arrange

        // Act
        BranchName result = BranchName.Generate("feat", 42, "");

        // Assert
        result.Value.ShouldBe("feat/42");
    }

    [Fact]
    public void WhenTitleIsWhitespace_ProducesPrefixAndNumberOnly()
    {
        // Arrange

        // Act
        BranchName result = BranchName.Generate("feat", 42, "   ");

        // Assert
        result.Value.ShouldBe("feat/42");
    }

    [Fact]
    public void WhenBranchPrefixIsInvalid_ThrowsArgumentException()
    {
        // Arrange
        string invalidPrefix = "Feat/my-type";

        // Act
        ArgumentException ex = Should.Throw<ArgumentException>(() => BranchName.Generate(invalidPrefix, 1, "title"));

        // Assert
        ex.ParamName.ShouldBe("branchPrefix");
    }
}
