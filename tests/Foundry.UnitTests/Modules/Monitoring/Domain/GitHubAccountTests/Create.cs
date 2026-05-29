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
        string secretKeyName = "GITHUB_TOKEN";
        Uri baseUrl = new("https://api.github.com");

        // Act
        GitHubAccount account = GitHubAccount.Create(name, secretKeyName, baseUrl);

        // Assert
        account.ShouldSatisfyAllConditions(
            () => account.Name.ShouldBe(name),
            () => account.SecretKeyName.ShouldBe(secretKeyName),
            () => account.BaseUrl.ShouldBe(baseUrl));
    }

    [Fact]
    public void WhenCreated_AssignsNewId()
    {
        // Arrange
        Uri baseUrl = new("https://api.github.com");

        // Act
        GitHubAccount a = GitHubAccount.Create("account-a", "KEY_A", baseUrl);
        GitHubAccount b = GitHubAccount.Create("account-b", "KEY_B", baseUrl);

        // Assert
        a.Id.ShouldNotBe(b.Id);
    }
}
