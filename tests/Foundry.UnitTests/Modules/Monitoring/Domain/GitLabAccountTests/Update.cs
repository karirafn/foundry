using Foundry.Modules.Monitoring.Domain.Entities;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.GitLabAccountTests;

public sealed class Update
{
    [Fact]
    public void WhenTokenIsProvided_UpdatesAllProperties()
    {
        // Arrange
        Uri baseUrl = new("https://gitlab.com");
        GitLabAccount account = GitLabAccount.Create("original-name", "original-token", baseUrl);
        Uri newBaseUrl = new("https://gitlab.example.com");

        // Act
        account.Update("new-name", "new-token", newBaseUrl);

        // Assert
        account.ShouldSatisfyAllConditions(
            () => account.Name.ShouldBe("new-name"),
            () => account.Token.ShouldBe("new-token"),
            () => account.BaseUrl.ShouldBe(newBaseUrl));
    }

    [Fact]
    public void WhenTokenIsNull_KeepsExistingToken()
    {
        // Arrange
        Uri baseUrl = new("https://gitlab.com");
        GitLabAccount account = GitLabAccount.Create("original-name", "original-token", baseUrl);
        Uri newBaseUrl = new("https://gitlab.example.com");

        // Act
        account.Update("new-name", null, newBaseUrl);

        // Assert
        account.Token.ShouldBe("original-token");
    }

    [Fact]
    public void WhenBaseUrlSchemeIsNotHttps_ThrowsArgumentException()
    {
        // Arrange
        Uri baseUrl = new("https://gitlab.com");
        GitLabAccount account = GitLabAccount.Create("original-name", "original-token", baseUrl);
        Uri invalidBaseUrl = new("http://gitlab.example.com");

        // Act
        Action act = () => account.Update("new-name", "new-token", invalidBaseUrl);

        // Assert
        Should.Throw<ArgumentException>(act);
    }
}
