using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.GitLabCredentialTests;

public sealed class ApiBaseUrl
{
    [Fact]
    public void WhenBaseUrlIsGitLabCom_ReturnsApiV4Url()
    {
        // Arrange
        GitLabCredential credential = GitLabCredential.Create("my-account", "my-token", BaseUrl.Create("https://gitlab.com").ValueOrThrow());

        // Act
        Uri result = credential.ApiBaseUrl;

        // Assert
        result.ShouldBe(new Uri("https://gitlab.com/api/v4"));
    }

    [Fact]
    public void WhenBaseUrlIsSelfHosted_ReturnsApiV4Url()
    {
        // Arrange
        GitLabCredential credential = GitLabCredential.Create("my-account", "my-token", BaseUrl.Create("https://gitlab.example.com").ValueOrThrow());

        // Act
        Uri result = credential.ApiBaseUrl;

        // Assert
        result.ShouldBe(new Uri("https://gitlab.example.com/api/v4"));
    }
}
