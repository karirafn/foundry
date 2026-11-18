using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Issues.Features;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Features.ProviderWebUrlTests;

public sealed class Commit
{
    [Theory]
    [InlineData("https://github.com/owner/repo/issues/7", "https://github.com/owner/repo/commit/bbb222")]
    [InlineData("https://gitlab.com/group/project/-/issues/7", "https://gitlab.com/group/project/-/commit/bbb222")]
    public void ForAProviderIssueUrl_ReturnsTheCommitPage(string issueUrl, string expected)
    {
        // Arrange
        ProviderUrl url = ProviderUrl.Create(issueUrl).ValueOrThrow();

        // Act
        string? result = ProviderWebUrl.Commit(url, "bbb222");

        // Assert
        result.ShouldBe(expected);
    }
}
