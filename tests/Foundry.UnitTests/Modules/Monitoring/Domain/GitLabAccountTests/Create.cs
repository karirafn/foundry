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
    // only way to test the backstop in GitLabCredential.Create is to bypass BaseUrl validation
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
        Action act = () => GitLabCredential.Create("account", "token", baseUrl);

        // Assert
        Should.Throw<ArgumentException>(act);
    }


    [Fact]
    public void WhenBaseUrlCarriesFragment_ThrowsArgumentException()
    {
        // Arrange
        BaseUrl baseUrl = BuildTamperedBaseUrl("https://gitlab.example.com/#section");

        // Act
        Action act = () => GitLabCredential.Create("account", "token", baseUrl);

        // Assert
        Should.Throw<ArgumentException>(act);
    }

    [Fact]
    public void WhenAllParametersAreValid_ReturnsGitLabCredentialWithCorrectProperties()
    {
        // Arrange
        string name = "my-gitlab-account";
        string token = "glpat_mytoken";
        BaseUrl baseUrl = BaseUrl.Create("https://gitlab.com").ValueOrThrow();

        // Act
        GitLabCredential credential = GitLabCredential.Create(name, token, baseUrl);

        // Assert
        credential.ShouldSatisfyAllConditions(
            () => credential.Name.ShouldBe(name),
            () => credential.Token.ShouldBe(token),
            () => credential.BaseUrl.Value.ShouldBe(new Uri("https://gitlab.com")),
            () => credential.Host.ShouldBe("gitlab.com"));
    }

    [Fact]
    public void WhenCreated_AssignsNewId()
    {
        // Arrange
        BaseUrl baseUrl = BaseUrl.Create("https://gitlab.com").ValueOrThrow();

        // Act
        GitLabCredential a = GitLabCredential.Create("account-a", "token-a", baseUrl);
        GitLabCredential b = GitLabCredential.Create("account-b", "token-b", baseUrl);

        // Assert
        a.Id.ShouldNotBe(b.Id);
    }

    [Fact]
    public void WhenTokenIsNull_ReturnsGitLabCredentialWithNullToken()
    {
        // Arrange
        string name = "my-gitlab-account";
        BaseUrl baseUrl = BaseUrl.Create("https://gitlab.com").ValueOrThrow();

        // Act
        GitLabCredential credential = GitLabCredential.Create(name, null, baseUrl);

        // Assert
        credential.Token.ShouldBeNull();
    }
}
