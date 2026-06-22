using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Testing;

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
        BaseUrl baseUrl = BaseUrl.Create("https://github.com").ValueOrThrow();

        // Act
        GitHubAccount account = GitHubAccount.Create(name, token, baseUrl);

        // Assert
        account.ShouldSatisfyAllConditions(
            () => account.Name.ShouldBe(name),
            () => account.Token.ShouldBe(token),
            () => account.BaseUrl.Value.ShouldBe(new Uri("https://github.com")));
    }

    [Fact]
    public void WhenCreated_AssignsNewId()
    {
        // Arrange
        BaseUrl baseUrl = BaseUrlFactory.Create("https://github.com");

        // Act
        GitHubAccount a = GitHubAccount.Create("account-a", "token-a", baseUrl);
        GitHubAccount b = GitHubAccount.Create("account-b", "token-b", baseUrl);

        // Assert
        a.Id.ShouldNotBe(b.Id);
    }

    [Fact]
    public void WhenTokenIsNull_ReturnsGitHubAccountWithNullToken()
    {
        // Arrange
        string name = "my-github-account";
        BaseUrl baseUrl = BaseUrlFactory.Create("https://github.com");

        // Act
        GitHubAccount account = GitHubAccount.Create(name, null, baseUrl);

        // Assert
        account.Token.ShouldBeNull();
    }
}
