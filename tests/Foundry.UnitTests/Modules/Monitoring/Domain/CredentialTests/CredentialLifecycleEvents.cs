using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.Events;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.CredentialTests;

public sealed class CredentialLifecycleEvents
{
    private static string Token(string suffix) => string.Concat("ghp_", suffix);

    [Fact]
    public void WhenAccountIsCreatedWithoutAToken_RaisesNothing()
    {
        // Act
        GitHubCredential credential = GitHubCredential.Create(
            AccountName.Create("my-org").ValueOrThrow(),
            null,
            BaseUrl.Create("https://github.com").ValueOrThrow());

        // Assert
        credential.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void WhenUpdateLeavesTheTokenUnchanged_RaisesNothing()
    {
        // Arrange
        GitHubCredential credential = GitHubCredential.Create(
            AccountName.Create("my-org").ValueOrThrow(),
            new IssuedToken(Token("a"), new TokenExpiry.NotChecked()),
            BaseUrl.Create("https://github.com").ValueOrThrow());
        credential.ClearDomainEvents();

        // Act
        credential.Update(AccountName.Create("renamed").ValueOrThrow(), null, BaseUrl.Create("https://github.com").ValueOrThrow());

        // Assert
        credential.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void WhenAccountIsDeleted_RaisesCredentialDeleted()
    {
        // Arrange
        GitLabCredential credential = GitLabCredential.Create(
            AccountName.Create("my-group").ValueOrThrow(),
            new IssuedToken(Token("a"), new TokenExpiry.NotChecked()),
            BaseUrl.Create("https://gitlab.com").ValueOrThrow());
        credential.ClearDomainEvents();

        // Act
        credential.Delete();

        // Assert
        credential.DomainEvents.ShouldBe([new CredentialDeleted(credential.Id)]);
    }
}
