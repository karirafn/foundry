using Foundry.Modules.Monitoring.Domain.Entities;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.GitHubAccountTests;

public sealed class Create
{
    [Fact]
    public void WhenAllParametersAreValid_ReturnsGitHubAccountWithCorrectProperties()
    {
        // Arrange
        string name = "my-github-account";
        string token = "ghp_mytoken";
        Uri baseUrl = new("https://github.com");

        // Act
        GitHubAccount account = GitHubAccount.Create(name, token, baseUrl);

        // Assert
        account.ShouldSatisfyAllConditions(
            () => account.Name.ShouldBe(name),
            () => account.Token.ShouldBe(token),
            () => account.BaseUrl.ShouldBe(baseUrl));
    }

    [Fact]
    public void WhenCreated_AssignsNewId()
    {
        // Arrange
        Uri baseUrl = new("https://github.com");

        // Act
        GitHubAccount a = GitHubAccount.Create("account-a", "token-a", baseUrl);
        GitHubAccount b = GitHubAccount.Create("account-b", "token-b", baseUrl);

        // Assert
        a.Id.ShouldNotBe(b.Id);
    }

    [Fact]
    public void WhenBaseUrlSchemeIsNotHttps_ThrowsArgumentException()
    {
        // Arrange
        Uri baseUrl = new("http://github.example.com");

        // Act
        Action act = () => GitHubAccount.Create("my-account", "my-token", baseUrl);

        // Assert
        Should.Throw<ArgumentException>(act);
    }

    [Fact]
    public void WhenTokenIsNull_ReturnsGitHubAccountWithNullToken()
    {
        // Arrange
        string name = "my-github-account";
        Uri baseUrl = new("https://github.com");

        // Act
        GitHubAccount account = GitHubAccount.Create(name, null, baseUrl);

        // Assert
        account.Token.ShouldBeNull();
    }
}
