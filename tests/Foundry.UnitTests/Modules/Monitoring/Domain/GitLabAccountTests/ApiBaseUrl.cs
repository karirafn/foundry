using Foundry.Modules.Monitoring.Domain.Entities;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.GitLabAccountTests;

public sealed class ApiBaseUrl
{
    [Fact]
    public void WhenBaseUrlIsGitLabCom_ReturnsApiV4Url()
    {
        // Arrange
        GitLabAccount account = GitLabAccount.Create("my-account", "my-token", new Uri("https://gitlab.com"));

        // Act
        Uri result = account.ApiBaseUrl;

        // Assert
        result.ShouldBe(new Uri("https://gitlab.com/api/v4"));
    }

    [Fact]
    public void WhenBaseUrlIsSelfHosted_ReturnsApiV4Url()
    {
        // Arrange
        GitLabAccount account = GitLabAccount.Create("my-account", "my-token", new Uri("https://gitlab.example.com"));

        // Act
        Uri result = account.ApiBaseUrl;

        // Assert
        result.ShouldBe(new Uri("https://gitlab.example.com/api/v4"));
    }
}
