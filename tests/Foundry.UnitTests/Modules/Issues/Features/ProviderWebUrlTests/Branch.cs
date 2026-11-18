using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Issues.Features;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Features.ProviderWebUrlTests;

public sealed class Branch
{
    [Theory]
    [InlineData("https://github.com/owner/repo/issues/7", "feat/7-x", "https://github.com/owner/repo/tree/feat/7-x")]
    [InlineData("https://gitlab.com/group/project/-/issues/7", "feat/7-x", "https://gitlab.com/group/project/-/tree/feat/7-x")]
    public void ForAProviderIssueUrl_ReturnsTheBranchPage(string issueUrl, string branchName, string expected)
    {
        // Arrange
        ProviderUrl url = ProviderUrl.Create(issueUrl).ValueOrThrow();

        // Act
        string? result = ProviderWebUrl.Branch(url, branchName);

        // Assert
        result.ShouldBe(expected);
    }

    [Fact]
    public void ForAnUnknownUrlShape_ReturnsNull()
    {
        // Arrange
        ProviderUrl url = ProviderUrl.Create("https://example.com/owner/repo").ValueOrThrow();

        // Act
        string? result = ProviderWebUrl.Branch(url, "main");

        // Assert
        result.ShouldBeNull();
    }
}
