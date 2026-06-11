using Foundry.Modules.Monitoring.Domain.Entities;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.GitHubAccountTests;

public sealed class Update
{
    [Fact]
    public void WhenAllParametersAreValid_UpdatesProperties()
    {
        // Arrange
        GitHubAccount account = GitHubAccount.Create(
            "original-name",
            "original-token",
            new Uri("https://github.com"));

        // Act
        account.Update("updated-name", "new-token", new Uri("https://github.example.com"));

        // Assert
        account.ShouldSatisfyAllConditions(
            () => account.Name.ShouldBe("updated-name"),
            () => account.Token.ShouldBe("new-token"),
            () => account.BaseUrl.ShouldBe(new Uri("https://github.example.com")));
    }

    [Fact]
    public void WhenTokenIsNull_DoesNotUpdateToken()
    {
        // Arrange
        GitHubAccount account = GitHubAccount.Create(
            "my-account",
            "existing-token",
            new Uri("https://github.com"));

        // Act
        account.Update("updated-name", null, new Uri("https://github.com"));

        // Assert
        account.ShouldSatisfyAllConditions(
            () => account.Name.ShouldBe("updated-name"),
            () => account.Token.ShouldBe("existing-token"));
    }

    [Fact]
    public void WhenBaseUrlSchemeIsNotHttps_ThrowsArgumentException()
    {
        // Arrange
        GitHubAccount account = GitHubAccount.Create(
            "my-account",
            "my-token",
            new Uri("https://github.com"));

        // Act
        Action act = () => account.Update("my-account", "my-token", new Uri("http://insecure.example.com"));

        // Assert
        Should.Throw<ArgumentException>(act);
    }
}
