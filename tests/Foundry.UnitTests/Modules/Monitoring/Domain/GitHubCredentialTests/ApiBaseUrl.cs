using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.GitHubCredentialTests;

public sealed class ApiBaseUrl
{
    [Fact]
    public void WhenBaseUrlIsGitHubDotCom_ReturnsApiGitHubCom()
    {
        // Arrange
        GitHubCredential credential = GitHubCredential.Create(AccountName.Create("my-account").ValueOrThrow(), new IssuedToken("GITHUB_TOKEN", new TokenExpiry.NotChecked()), BaseUrl.Create("https://github.com").ValueOrThrow());

        // Act
        Uri apiBaseUrl = credential.ApiBaseUrl;

        // Assert
        apiBaseUrl.ShouldBe(new Uri("https://api.github.com"));
    }

    [Fact]
    public void WhenBaseUrlIsGitHubEnterprise_ReturnsBaseUrlWithApiV3Suffix()
    {
        // Arrange
        GitHubCredential credential = GitHubCredential.Create(AccountName.Create("ghe-account").ValueOrThrow(), new IssuedToken("GHE_TOKEN", new TokenExpiry.NotChecked()), BaseUrl.Create("https://github.example.com").ValueOrThrow());

        // Act
        Uri apiBaseUrl = credential.ApiBaseUrl;

        // Assert
        apiBaseUrl.ShouldBe(new Uri("https://github.example.com/api/v3/"));
    }

    [Fact]
    public void WhenBaseUrlIsGitHubEnterpriseWithTrailingPath_PreservesSubPath()
    {
        // Arrange
        GitHubCredential credential = GitHubCredential.Create(AccountName.Create("ghe-account").ValueOrThrow(), new IssuedToken("GHE_TOKEN", new TokenExpiry.NotChecked()), BaseUrl.Create("https://corp.example.com/github").ValueOrThrow());

        // Act
        Uri apiBaseUrl = credential.ApiBaseUrl;

        // Assert
        apiBaseUrl.ShouldBe(new Uri("https://corp.example.com/github/api/v3/"));
    }
}
