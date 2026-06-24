using System.Reflection;

using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.GitLabAccountTests;

public sealed class Create
{
    // BaseUrl.Create and FromPersistedString both reject query strings and fragments, so the
    // only way to test the backstop in GitLabAccount.Create is to bypass BaseUrl validation
    // using reflection — constructing a BaseUrl whose inner Uri carries query/fragment.
    private static BaseUrl BuildTamperedBaseUrl(string rawUrl)
    {
        Uri uri = new(rawUrl);
        ConstructorInfo ctor = typeof(BaseUrl)
            .GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, [typeof(Uri)])!;
        return (BaseUrl)ctor.Invoke([uri]);
    }

    [Fact]
    public void WhenBaseUrlCarriesQueryString_ThrowsArgumentException()
    {
        // Arrange
        BaseUrl baseUrl = BuildTamperedBaseUrl("https://gitlab.example.com/?x=1");

        // Act
        Action act = () => GitLabAccount.Create("account", "token", baseUrl);

        // Assert
        Should.Throw<ArgumentException>(act);
    }


    [Fact]
    public void WhenBaseUrlCarriesFragment_ThrowsArgumentException()
    {
        // Arrange
        BaseUrl baseUrl = BuildTamperedBaseUrl("https://gitlab.example.com/#section");

        // Act
        Action act = () => GitLabAccount.Create("account", "token", baseUrl);

        // Assert
        Should.Throw<ArgumentException>(act);
    }

    [Fact]
    public void WhenAllParametersAreValid_ReturnsGitLabAccountWithCorrectProperties()
    {
        // Arrange
        string name = "my-gitlab-account";
        string token = "glpat_mytoken";
        BaseUrl baseUrl = BaseUrl.Create("https://gitlab.com").ValueOrThrow();

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
        BaseUrl baseUrl = BaseUrl.Create("https://gitlab.com").ValueOrThrow();

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
        BaseUrl baseUrl = BaseUrl.Create("https://gitlab.com").ValueOrThrow();

        // Act
        GitLabAccount account = GitLabAccount.Create(name, null, baseUrl);

        // Assert
        account.Token.ShouldBeNull();
    }
}
