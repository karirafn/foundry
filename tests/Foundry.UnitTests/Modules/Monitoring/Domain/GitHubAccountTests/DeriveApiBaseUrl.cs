using Foundry.Modules.Monitoring.Domain.Entities;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.GitHubAccountTests;

public sealed class DeriveApiBaseUrl
{
    [Fact]
    public void WhenBaseUrlIsGitHubDotCom_ReturnsApiGitHubCom()
    {
        // Arrange
        Uri baseUrl = new("https://github.com");

        // Act
        Uri apiBaseUrl = GitHubAccount.DeriveApiBaseUrl(baseUrl);

        // Assert
        apiBaseUrl.ShouldBe(new Uri("https://api.github.com"));
    }

    [Fact]
    public void WhenBaseUrlIsGitHubEnterprise_ReturnsBaseUrlWithApiV3Suffix()
    {
        // Arrange
        Uri baseUrl = new("https://github.example.com");

        // Act
        Uri apiBaseUrl = GitHubAccount.DeriveApiBaseUrl(baseUrl);

        // Assert
        apiBaseUrl.ShouldBe(new Uri("https://github.example.com/api/v3/"));
    }

    [Fact]
    public void WhenBaseUrlIsGitHubEnterpriseWithTrailingPath_PreservesSubPath()
    {
        // Arrange
        Uri baseUrl = new("https://corp.example.com/github");

        // Act
        Uri apiBaseUrl = GitHubAccount.DeriveApiBaseUrl(baseUrl);

        // Assert
        apiBaseUrl.ShouldBe(new Uri("https://corp.example.com/github/api/v3/"));
    }
}
