using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.GitLabAccountTests;

public sealed class ApiBaseUrl
{
    private static BaseUrl MakeBaseUrl(string url) =>
        ((Result<BaseUrl>.Success)BaseUrl.Create(url)).Value;

    [Fact]
    public void WhenBaseUrlIsGitLabCom_ReturnsApiV4Url()
    {
        // Arrange
        GitLabAccount account = GitLabAccount.Create("my-account", "my-token", MakeBaseUrl("https://gitlab.com"));

        // Act
        Uri result = account.ApiBaseUrl;

        // Assert
        result.ShouldBe(new Uri("https://gitlab.com/api/v4"));
    }

    [Fact]
    public void WhenBaseUrlIsSelfHosted_ReturnsApiV4Url()
    {
        // Arrange
        GitLabAccount account = GitLabAccount.Create("my-account", "my-token", MakeBaseUrl("https://gitlab.example.com"));

        // Act
        Uri result = account.ApiBaseUrl;

        // Assert
        result.ShouldBe(new Uri("https://gitlab.example.com/api/v4"));
    }
}
