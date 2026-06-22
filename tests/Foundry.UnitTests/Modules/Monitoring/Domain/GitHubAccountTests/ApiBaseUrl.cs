using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.GitHubAccountTests;

public sealed class ApiBaseUrl
{
    [Fact]
    public void WhenBaseUrlIsGitHubDotCom_ReturnsApiGitHubCom()
    {
        // Arrange
        GitHubAccount account = GitHubAccount.Create("my-account", "GITHUB_TOKEN", BaseUrl.Create("https://github.com").ValueOrThrow());

        // Act
        Uri apiBaseUrl = account.ApiBaseUrl;

        // Assert
        apiBaseUrl.ShouldBe(new Uri("https://api.github.com"));
    }

    [Fact]
    public void WhenBaseUrlIsGitHubEnterprise_ReturnsBaseUrlWithApiV3Suffix()
    {
        // Arrange
        GitHubAccount account = GitHubAccount.Create("ghe-account", "GHE_TOKEN", BaseUrl.Create("https://github.example.com").ValueOrThrow());

        // Act
        Uri apiBaseUrl = account.ApiBaseUrl;

        // Assert
        apiBaseUrl.ShouldBe(new Uri("https://github.example.com/api/v3/"));
    }

    [Fact]
    public void WhenBaseUrlIsGitHubEnterpriseWithTrailingPath_PreservesSubPath()
    {
        // Arrange
        GitHubAccount account = GitHubAccount.Create("ghe-account", "GHE_TOKEN", BaseUrl.Create("https://corp.example.com/github").ValueOrThrow());

        // Act
        Uri apiBaseUrl = account.ApiBaseUrl;

        // Assert
        apiBaseUrl.ShouldBe(new Uri("https://corp.example.com/github/api/v3/"));
    }
}
