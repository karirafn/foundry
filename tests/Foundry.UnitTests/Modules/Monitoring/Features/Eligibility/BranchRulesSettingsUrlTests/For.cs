using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Eligibility;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.Eligibility.BranchRulesSettingsUrlTests;

public sealed class For
{
    private static RepositorySlug Slug => RepositorySlug.Create("owner/repo").ValueOrThrow();

    [Theory]
    [InlineData("https://github.com", "https://github.com/owner/repo/settings/rules")]
    [InlineData("https://ghe.example.com/", "https://ghe.example.com/owner/repo/settings/rules")]
    public void ForGitHub_PointsAtTheRulesetsPage(string baseUrl, string expected)
    {
        // Arrange
        GitHubCredential account = GitHubCredential.Create("acct", "token", BaseUrl.Create(baseUrl).ValueOrThrow());

        // Act
        string url = BranchRulesSettingsUrl.For(account, Slug);

        // Assert
        url.ShouldBe(expected);
    }

    [Fact]
    public void ForGitLab_PointsAtTheProtectedBranchesSettings()
    {
        // Arrange
        GitLabCredential account = GitLabCredential.Create(
            "acct",
            "token",
            BaseUrl.Create("https://gitlab.example.com").ValueOrThrow());

        // Act
        string url = BranchRulesSettingsUrl.For(account, Slug);

        // Assert
        url.ShouldBe("https://gitlab.example.com/owner/repo/-/settings/repository#js-protected-branches-settings");
    }
}
