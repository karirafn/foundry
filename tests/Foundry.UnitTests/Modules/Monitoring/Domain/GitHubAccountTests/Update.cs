using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.GitHubAccountTests;

public sealed class Update
{
    [Fact]
    public void WhenAllParametersAreValid_UpdatesProperties()
    {
        // Arrange
        GitHubAccount account = GitHubAccount.Create(
            "original-name",
            "original-token",
            BaseUrl.Create("https://github.com").ValueOrThrow());

        // Act
        account.Update("updated-name", "new-token", BaseUrl.Create("https://github.example.com").ValueOrThrow());

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
            BaseUrl.Create("https://github.com").ValueOrThrow());

        // Act
        account.Update("updated-name", null, BaseUrl.Create("https://github.com").ValueOrThrow());

        // Assert
        account.ShouldSatisfyAllConditions(
            () => account.Name.ShouldBe("updated-name"),
            () => account.Token.ShouldBe("existing-token"));
    }
}
