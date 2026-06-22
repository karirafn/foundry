using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.GitLabAccountTests;

public sealed class Create
{
    private static BaseUrl MakeBaseUrl(string url) =>
        ((Result<BaseUrl>.Success)BaseUrl.Create(url)).Value;

    [Fact]
    public void WhenAllParametersAreValid_ReturnsGitLabAccountWithCorrectProperties()
    {
        // Arrange
        string name = "my-gitlab-account";
        string token = "glpat_mytoken";
        BaseUrl baseUrl = MakeBaseUrl("https://gitlab.com");

        // Act
        GitLabAccount account = GitLabAccount.Create(name, token, baseUrl);

        // Assert
        account.ShouldSatisfyAllConditions(
            () => account.Name.ShouldBe(name),
            () => account.Token.ShouldBe(token),
            () => account.BaseUrl.Value.ShouldBe(new Uri("https://gitlab.com")));
    }

    [Fact]
    public void WhenCreated_AssignsNewId()
    {
        // Arrange
        BaseUrl baseUrl = MakeBaseUrl("https://gitlab.com");

        // Act
        GitLabAccount a = GitLabAccount.Create("account-a", "token-a", baseUrl);
        GitLabAccount b = GitLabAccount.Create("account-b", "token-b", baseUrl);

        // Assert
        a.Id.ShouldNotBe(b.Id);
    }

    [Fact]
    public void WhenTokenIsNull_ReturnsGitLabAccountWithNullToken()
    {
        // Arrange
        string name = "my-gitlab-account";
        BaseUrl baseUrl = MakeBaseUrl("https://gitlab.com");

        // Act
        GitLabAccount account = GitLabAccount.Create(name, null, baseUrl);

        // Assert
        account.Token.ShouldBeNull();
    }
}
