using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.GitHubAccountTests;

public sealed class DeriveApiBaseUrl
{
    private static BaseUrl MakeBaseUrl(string url) =>
        ((Result<BaseUrl>.Success)BaseUrl.Create(url)).Value;

    [Fact]
    public void WhenBaseUrlIsGitHubDotCom_ReturnsApiGitHubCom()
    {
        // Arrange
        BaseUrl baseUrl = MakeBaseUrl("https://github.com");

        // Act
        Uri apiBaseUrl = GitHubAccount.DeriveApiBaseUrl(baseUrl);

        // Assert
        apiBaseUrl.ShouldBe(new Uri("https://api.github.com"));
    }

    [Fact]
    public void WhenBaseUrlIsGitHubEnterprise_ReturnsBaseUrlWithApiV3Suffix()
    {
        // Arrange
        BaseUrl baseUrl = MakeBaseUrl("https://github.example.com");

        // Act
        Uri apiBaseUrl = GitHubAccount.DeriveApiBaseUrl(baseUrl);

        // Assert
        apiBaseUrl.ShouldBe(new Uri("https://github.example.com/api/v3/"));
    }

    [Fact]
    public void WhenBaseUrlIsGitHubEnterpriseWithTrailingPath_PreservesSubPath()
    {
        // Arrange
        BaseUrl baseUrl = MakeBaseUrl("https://corp.example.com/github");

        // Act
        Uri apiBaseUrl = GitHubAccount.DeriveApiBaseUrl(baseUrl);

        // Assert
        apiBaseUrl.ShouldBe(new Uri("https://corp.example.com/github/api/v3/"));
    }
}
