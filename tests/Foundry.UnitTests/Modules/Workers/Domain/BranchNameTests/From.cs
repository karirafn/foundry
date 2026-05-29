using Foundry.Modules.Workers.Domain;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Domain.BranchNameTests;

public sealed class From
{
    [Fact]
    public void WhenCalledWithValidString_ReturnsBranchNameWithSameValue()
    {
        // Arrange
        const string rawName = "feat/issue-5";

        // Act
        BranchName branchName = BranchName.From(rawName);

        // Assert
        branchName.Value.ShouldBe(rawName);
    }

    [Fact]
    public void WhenCalledWithSameString_ProducesEqualBranchNames()
    {
        // Arrange
        const string rawName = "feat/add-login";

        // Act
        BranchName a = BranchName.From(rawName);
        BranchName b = BranchName.From(rawName);

        // Assert
        a.ShouldBe(b);
    }

    [Fact]
    public void WhenCalledWithDifferentStrings_ProducesDistinctBranchNames()
    {
        // Arrange

        // Act
        BranchName a = BranchName.From("feat/branch-1");
        BranchName b = BranchName.From("feat/branch-2");

        // Assert
        a.ShouldNotBe(b);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void WhenCalledWithEmptyOrWhitespace_ThrowsArgumentException(string value)
    {
        // Arrange

        // Act / Assert
        Should.Throw<ArgumentException>(() => BranchName.From(value));
    }
}
