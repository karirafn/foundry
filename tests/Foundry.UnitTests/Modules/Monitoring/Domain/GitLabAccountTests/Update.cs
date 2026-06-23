using System.Reflection;

using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.GitLabAccountTests;

public sealed class Update
{
    // BaseUrl.Create and FromPersistedString both reject query strings and fragments, so the
    // only way to test the backstop in GitLabAccount.Update is to bypass BaseUrl validation
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
        GitLabAccount account = GitLabAccount.Create("name", "token", BaseUrl.Create("https://gitlab.com").ValueOrThrow());
        BaseUrl baseUrl = BuildTamperedBaseUrl("https://gitlab.example.com/?x=1");

        // Act
        Action act = () => account.Update("name", "token", baseUrl);

        // Assert
        Should.Throw<ArgumentException>(act);
    }

    [Fact]
    public void WhenBaseUrlCarriesFragment_ThrowsArgumentException()
    {
        // Arrange
        GitLabAccount account = GitLabAccount.Create("name", "token", BaseUrl.Create("https://gitlab.com").ValueOrThrow());
        BaseUrl baseUrl = BuildTamperedBaseUrl("https://gitlab.example.com/#section");

        // Act
        Action act = () => account.Update("name", "token", baseUrl);

        // Assert
        Should.Throw<ArgumentException>(act);
    }


    [Fact]
    public void WhenTokenIsProvided_UpdatesAllProperties()
    {
        // Arrange
        GitLabAccount account = GitLabAccount.Create("original-name", "original-token", BaseUrl.Create("https://gitlab.com").ValueOrThrow());
        BaseUrl newBaseUrl = BaseUrl.Create("https://gitlab.example.com").ValueOrThrow();

        // Act
        account.Update("new-name", "new-token", newBaseUrl);

        // Assert
        account.ShouldSatisfyAllConditions(
            () => account.Name.ShouldBe("new-name"),
            () => account.Token.ShouldBe("new-token"),
            () => account.BaseUrl.Value.ShouldBe(new Uri("https://gitlab.example.com")));
    }

    [Fact]
    public void WhenTokenIsNull_KeepsExistingToken()
    {
        // Arrange
        GitLabAccount account = GitLabAccount.Create("original-name", "original-token", BaseUrl.Create("https://gitlab.com").ValueOrThrow());
        BaseUrl newBaseUrl = BaseUrl.Create("https://gitlab.example.com").ValueOrThrow();

        // Act
        account.Update("new-name", null, newBaseUrl);

        // Assert
        account.Token.ShouldBe("original-token");
    }
}
