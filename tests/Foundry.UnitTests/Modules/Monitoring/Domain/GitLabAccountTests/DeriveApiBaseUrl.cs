using Foundry.Modules.Monitoring.Domain.Entities;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.GitLabAccountTests;

public sealed class DeriveApiBaseUrl
{
    [Fact]
    public void WhenBaseUrlIsGitLabCom_ReturnsGitLabApiV4Url()
    {
        // Arrange
        Uri baseUrl = new("https://gitlab.com");

        // Act
        Uri result = GitLabAccount.DeriveApiBaseUrl(baseUrl);

        // Assert
        result.ShouldBe(new Uri("https://gitlab.com/api/v4"));
    }

    [Fact]
    public void WhenBaseUrlIsSelfHosted_ReturnsApiV4Url()
    {
        // Arrange
        Uri baseUrl = new("https://gitlab.example.com");

        // Act
        Uri result = GitLabAccount.DeriveApiBaseUrl(baseUrl);

        // Assert
        result.ShouldBe(new Uri("https://gitlab.example.com/api/v4"));
    }

    [Fact]
    public void WhenBaseUrlHasTrailingSlash_DoesNotDoubleSlash()
    {
        // Arrange
        Uri baseUrl = new("https://gitlab.example.com/");

        // Act
        Uri result = GitLabAccount.DeriveApiBaseUrl(baseUrl);

        // Assert
        result.ShouldBe(new Uri("https://gitlab.example.com/api/v4"));
    }
}
