using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.GitHubAccountTests;

public sealed class DeriveApiBaseUrl
{
    [Fact]
    public void WhenBaseUrlIsGitHubDotCom_ReturnsApiGitHubCom()
    {
        // Arrange
        BaseUrl baseUrl = BaseUrlFactory.Create("https://github.com");

        // Act
        Uri apiBaseUrl = GitHubAccount.DeriveApiBaseUrl(baseUrl);

        // Assert
        apiBaseUrl.ShouldBe(new Uri("https://api.github.com"));
    }

    [Fact]
    public void WhenBaseUrlIsGitHubEnterprise_ReturnsBaseUrlWithApiV3Suffix()
    {
        // Arrange
        BaseUrl baseUrl = BaseUrlFactory.Create("https://github.example.com");

        // Act
        Uri apiBaseUrl = GitHubAccount.DeriveApiBaseUrl(baseUrl);

        // Assert
        apiBaseUrl.ShouldBe(new Uri("https://github.example.com/api/v3/"));
    }

    [Fact]
    public void WhenBaseUrlIsGitHubEnterpriseWithTrailingPath_PreservesSubPath()
    {
        // Arrange
        BaseUrl baseUrl = BaseUrlFactory.Create("https://corp.example.com/github");

        // Act
        Uri apiBaseUrl = GitHubAccount.DeriveApiBaseUrl(baseUrl);

        // Assert
        apiBaseUrl.ShouldBe(new Uri("https://corp.example.com/github/api/v3/"));
    }
}
