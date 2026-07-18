using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.GitHubAccountTests;

public sealed class Create
{
    [Fact]
    public void WhenAllParametersAreValid_ReturnsGitHubCredentialWithCorrectProperties()
    {
        // Arrange
        string name = "my-github-account";
        string token = "ghp_mytoken";
        BaseUrl baseUrl = BaseUrl.Create("https://github.com").ValueOrThrow();

        // Act
        GitHubCredential credential = GitHubCredential.Create(name, token, baseUrl);

        // Assert
        credential.ShouldSatisfyAllConditions(
            () => credential.Name.ShouldBe(name),
            () => credential.Token.ShouldBe(token),
            () => credential.BaseUrl.Value.ShouldBe(new Uri("https://github.com")),
            () => credential.Host.ShouldBe("github.com"));
    }

    [Fact]
    public void WhenCreated_AssignsNewId()
    {
        // Arrange
        BaseUrl baseUrl = BaseUrl.Create("https://github.com").ValueOrThrow();

        // Act
        GitHubCredential a = GitHubCredential.Create("account-a", "token-a", baseUrl);
        GitHubCredential b = GitHubCredential.Create("account-b", "token-b", baseUrl);

        // Assert
        a.Id.ShouldNotBe(b.Id);
    }

    [Fact]
    public void WhenTokenIsNull_ReturnsGitHubCredentialWithNullToken()
    {
        // Arrange
        string name = "my-github-account";
        BaseUrl baseUrl = BaseUrl.Create("https://github.com").ValueOrThrow();

        // Act
        GitHubCredential credential = GitHubCredential.Create(name, null, baseUrl);

        // Assert
        credential.Token.ShouldBeNull();
    }
}
