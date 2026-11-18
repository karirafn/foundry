using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Issues.Features;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Features.ProviderWebUrlTests;

public sealed class Compare
{
    [Theory]
    [InlineData("https://github.com/owner/repo/issues/7", "https://github.com/owner/repo/compare/aaa111...bbb222")]
    [InlineData("https://gitlab.com/group/sub/project/-/issues/7", "https://gitlab.com/group/sub/project/-/compare/aaa111...bbb222")]
    public void ForAProviderIssueUrl_ReturnsTheCompareView(string issueUrl, string expected)
    {
        // Arrange
        ProviderUrl url = ProviderUrl.Create(issueUrl).ValueOrThrow();

        // Act
        string? result = ProviderWebUrl.Compare(url, "aaa111", "bbb222");

        // Assert
        result.ShouldBe(expected);
    }

    [Fact]
    public void ForAnUnknownUrlShape_ReturnsNull()
    {
        // Arrange
        ProviderUrl url = ProviderUrl.Create("https://example.com/owner/repo").ValueOrThrow();

        // Act
        string? result = ProviderWebUrl.Compare(url, "aaa111", "bbb222");

        // Assert
        result.ShouldBeNull();
    }
}
