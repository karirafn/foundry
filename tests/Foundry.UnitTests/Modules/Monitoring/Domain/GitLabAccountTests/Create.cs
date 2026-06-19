using Foundry.Modules.Monitoring.Domain.Entities;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.GitLabAccountTests;

public sealed class Create
{
    [Fact]
    public void WhenAllParametersAreValid_ReturnsGitLabAccountWithCorrectProperties()
    {
        // Arrange
        string name = "my-gitlab-account";
        string token = "glpat_mytoken";
        Uri baseUrl = new("https://gitlab.com");

        // Act
        GitLabAccount account = GitLabAccount.Create(name, token, baseUrl);

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
        Uri baseUrl = new("https://gitlab.com");

        // Act
        GitLabAccount a = GitLabAccount.Create("account-a", "token-a", baseUrl);
        GitLabAccount b = GitLabAccount.Create("account-b", "token-b", baseUrl);

        // Assert
        a.Id.ShouldNotBe(b.Id);
    }

    [Fact]
    public void WhenBaseUrlSchemeIsNotHttps_ThrowsArgumentException()
    {
        // Arrange
        Uri baseUrl = new("http://gitlab.example.com");

        // Act
        Action act = () => GitLabAccount.Create("my-account", "my-token", baseUrl);

        // Assert
        Should.Throw<ArgumentException>(act);
    }

    [Fact]
    public void WhenTokenIsNull_ReturnsGitLabAccountWithNullToken()
    {
        // Arrange
        string name = "my-gitlab-account";
        Uri baseUrl = new("https://gitlab.com");

        // Act
        GitLabAccount account = GitLabAccount.Create(name, null, baseUrl);

        // Assert
        account.Token.ShouldBeNull();
    }
}
