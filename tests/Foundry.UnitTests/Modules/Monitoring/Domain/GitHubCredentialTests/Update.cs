using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Testing;

using ProviderToken = Foundry.Modules.Monitoring.Domain.ValueObjects.ProviderToken;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.GitHubCredentialTests;

public sealed class Update
{
    [Fact]
    public void WhenAllParametersAreValid_UpdatesProperties()
    {
        // Arrange
        GitHubCredential credential = GitHubCredential.Create(
            AccountName.Create("original-name").ValueOrThrow(),
            "original-token",
            BaseUrl.Create("https://github.com").ValueOrThrow());

        // Act
        credential.Update(AccountName.Create("updated-name").ValueOrThrow(), "new-token", BaseUrl.Create("https://github.example.com").ValueOrThrow());

        // Assert
        credential.ShouldSatisfyAllConditions(
            () => credential.Name.Value.ShouldBe("updated-name"),
            () => credential.Token.ShouldBe(new ProviderToken.Present("new-token")),
            () => credential.BaseUrl.Value.ShouldBe(new Uri("https://github.example.com")),
            () => credential.Host.ShouldBe("github.example.com"));
    }

    [Fact]
    public void WhenTokenIsNull_DoesNotUpdateToken()
    {
        // Arrange
        GitHubCredential credential = GitHubCredential.Create(
            AccountName.Create("my-account").ValueOrThrow(),
            "existing-token",
            BaseUrl.Create("https://github.com").ValueOrThrow());

        // Act
        credential.Update(AccountName.Create("updated-name").ValueOrThrow(), null, BaseUrl.Create("https://github.com").ValueOrThrow());

        // Assert
        credential.ShouldSatisfyAllConditions(
            () => credential.Name.Value.ShouldBe("updated-name"),
            () => credential.Token.ShouldBe(new ProviderToken.Present("existing-token")));
    }
}
