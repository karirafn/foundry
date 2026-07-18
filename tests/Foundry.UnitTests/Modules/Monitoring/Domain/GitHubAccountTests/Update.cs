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
        GitHubCredential credential = GitHubCredential.Create(
            "original-name",
            "original-token",
            BaseUrl.Create("https://github.com").ValueOrThrow());

        // Act
        credential.Update("updated-name", "new-token", BaseUrl.Create("https://github.example.com").ValueOrThrow());

        // Assert
        credential.ShouldSatisfyAllConditions(
            () => credential.Name.ShouldBe("updated-name"),
            () => credential.Token.ShouldBe("new-token"),
            () => credential.BaseUrl.Value.ShouldBe(new Uri("https://github.example.com")),
            () => credential.Host.ShouldBe("github.example.com"));
    }

    [Fact]
    public void WhenTokenIsNull_DoesNotUpdateToken()
    {
        // Arrange
        GitHubCredential credential = GitHubCredential.Create(
            "my-account",
            "existing-token",
            BaseUrl.Create("https://github.com").ValueOrThrow());

        // Act
        credential.Update("updated-name", null, BaseUrl.Create("https://github.com").ValueOrThrow());

        // Assert
        credential.ShouldSatisfyAllConditions(
            () => credential.Name.ShouldBe("updated-name"),
            () => credential.Token.ShouldBe("existing-token"));
    }
}
