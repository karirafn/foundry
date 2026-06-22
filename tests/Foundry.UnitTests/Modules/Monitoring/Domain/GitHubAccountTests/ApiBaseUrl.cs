using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.GitHubAccountTests;

public sealed class ApiBaseUrl
{
    private static BaseUrl MakeBaseUrl(string url) =>
        ((Result<BaseUrl>.Success)BaseUrl.Create(url)).Value;

    [Fact]
    public void WhenBaseUrlIsGitHubDotCom_ReturnsApiGitHubCom()
    {
        // Arrange
        GitHubAccount account = GitHubAccount.Create("my-account", "GITHUB_TOKEN", MakeBaseUrl("https://github.com"));

        // Act
        Uri apiBaseUrl = account.ApiBaseUrl;

        // Assert
        apiBaseUrl.ShouldBe(new Uri("https://api.github.com"));
    }

    [Fact]
    public void WhenBaseUrlIsGitHubEnterprise_ReturnsBaseUrlWithApiV3Suffix()
    {
        // Arrange
        GitHubAccount account = GitHubAccount.Create("ghe-account", "GHE_TOKEN", MakeBaseUrl("https://github.example.com"));

        // Act
        Uri apiBaseUrl = account.ApiBaseUrl;

        // Assert
        apiBaseUrl.ShouldBe(new Uri("https://github.example.com/api/v3/"));
    }

    [Fact]
    public void WhenBaseUrlIsGitHubEnterpriseWithTrailingPath_PreservesSubPath()
    {
        // Arrange
        GitHubAccount account = GitHubAccount.Create("ghe-account", "GHE_TOKEN", MakeBaseUrl("https://corp.example.com/github"));

        // Act
        Uri apiBaseUrl = account.ApiBaseUrl;

        // Assert
        apiBaseUrl.ShouldBe(new Uri("https://corp.example.com/github/api/v3/"));
    }
}
