using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.GitHubAccountTests;

public sealed class Update
{
    private static BaseUrl MakeBaseUrl(string url) =>
        ((Result<BaseUrl>.Success)BaseUrl.Create(url)).Value;

    [Fact]
    public void WhenAllParametersAreValid_UpdatesProperties()
    {
        // Arrange
        GitHubAccount account = GitHubAccount.Create(
            "original-name",
            "original-token",
            MakeBaseUrl("https://github.com"));

        // Act
        account.Update("updated-name", "new-token", MakeBaseUrl("https://github.example.com"));

        // Assert
        account.ShouldSatisfyAllConditions(
            () => account.Name.ShouldBe("updated-name"),
            () => account.Token.ShouldBe("new-token"),
            () => account.BaseUrl.Value.ShouldBe(new Uri("https://github.example.com")));
    }

    [Fact]
    public void WhenTokenIsNull_DoesNotUpdateToken()
    {
        // Arrange
        GitHubAccount account = GitHubAccount.Create(
            "my-account",
            "existing-token",
            MakeBaseUrl("https://github.com"));

        // Act
        account.Update("updated-name", null, MakeBaseUrl("https://github.com"));

        // Assert
        account.ShouldSatisfyAllConditions(
            () => account.Name.ShouldBe("updated-name"),
            () => account.Token.ShouldBe("existing-token"));
    }
}
