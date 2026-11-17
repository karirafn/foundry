using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.Events;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.CredentialTests;

public sealed class TokenValidityEvents
{
    private static readonly DateTimeOffset Detected = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);

    private static BaseUrl GitHubBaseUrl() => BaseUrl.Create("https://github.com").ValueOrThrow();

    private static BaseUrl GitLabBaseUrl() => BaseUrl.Create("https://gitlab.com").ValueOrThrow();

    private static string Token(string suffix) => string.Concat("ghp_", suffix);

    [Fact]
    public void WhenAccountIsCreated_RaisesNothing()
    {
        // Act
        GitHubCredential credential = GitHubCredential.Create(AccountName.Create("my-org").ValueOrThrow(), new IssuedToken(Token("a"), new TokenExpiry.NotChecked()), GitHubBaseUrl());

        // Assert
        credential.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void WhenValidTokenIsMarkedInvalid_RaisesCredentialTokenInvalidated()
    {
        // Arrange
        GitHubCredential credential = GitHubCredential.Create(AccountName.Create("my-org").ValueOrThrow(), new IssuedToken(Token("a"), new TokenExpiry.NotChecked()), GitHubBaseUrl());

        // Act
        credential.MarkTokenInvalid(Detected);

        // Assert
        credential.DomainEvents.ShouldBe([new CredentialTokenInvalidated(credential.Id)]);
    }

    [Fact]
    public void WhenInvalidTokenIsMarkedInvalidAgain_RaisesNothing()
    {
        // Arrange
        GitHubCredential credential = GitHubCredential.Create(AccountName.Create("my-org").ValueOrThrow(), new IssuedToken(Token("a"), new TokenExpiry.NotChecked()), GitHubBaseUrl());
        credential.MarkTokenInvalid(Detected);
        credential.ClearDomainEvents();

        // Act
        credential.MarkTokenInvalid(Detected.AddMinutes(20));

        // Assert
        credential.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void WhenInvalidTokenIsMarkedValid_RaisesCredentialTokenRevalidated()
    {
        // Arrange
        GitHubCredential credential = GitHubCredential.Create(AccountName.Create("my-org").ValueOrThrow(), new IssuedToken(Token("a"), new TokenExpiry.NotChecked()), GitHubBaseUrl());
        credential.MarkTokenInvalid(Detected);
        credential.ClearDomainEvents();

        // Act
        credential.MarkTokenValid();

        // Assert
        credential.DomainEvents.ShouldBe([new CredentialTokenRevalidated(credential.Id)]);
    }

    [Fact]
    public void WhenValidTokenIsMarkedValid_RaisesNothing()
    {
        // Arrange
        GitHubCredential credential = GitHubCredential.Create(AccountName.Create("my-org").ValueOrThrow(), new IssuedToken(Token("a"), new TokenExpiry.NotChecked()), GitHubBaseUrl());

        // Act
        credential.MarkTokenValid();

        // Assert
        credential.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void WhenInvalidGitHubAccountGetsNewToken_RaisesCredentialTokenRevalidated()
    {
        // Arrange
        GitHubCredential credential = GitHubCredential.Create(AccountName.Create("my-org").ValueOrThrow(), new IssuedToken(Token("a"), new TokenExpiry.NotChecked()), GitHubBaseUrl());
        credential.MarkTokenInvalid(Detected);
        credential.ClearDomainEvents();

        // Act
        credential.Update(AccountName.Create("my-org").ValueOrThrow(), new IssuedToken(Token("b"), new TokenExpiry.NotChecked()), GitHubBaseUrl());

        // Assert
        credential.DomainEvents.ShouldBe([new CredentialTokenRevalidated(credential.Id)]);
    }

    [Fact]
    public void WhenInvalidGitLabAccountGetsNewToken_RaisesCredentialTokenRevalidated()
    {
        // Arrange
        GitLabCredential credential = GitLabCredential.Create(AccountName.Create("my-group").ValueOrThrow(), new IssuedToken(Token("a"), new TokenExpiry.NotChecked()), GitLabBaseUrl());
        credential.MarkTokenInvalid(Detected);
        credential.ClearDomainEvents();

        // Act
        credential.Update(AccountName.Create("my-group").ValueOrThrow(), new IssuedToken(Token("b"), new TokenExpiry.NotChecked()), GitLabBaseUrl());

        // Assert
        credential.DomainEvents.ShouldBe([new CredentialTokenRevalidated(credential.Id)]);
    }

    [Fact]
    public void WhenValidAccountGetsNewToken_RaisesNothing()
    {
        // Arrange
        GitHubCredential credential = GitHubCredential.Create(AccountName.Create("my-org").ValueOrThrow(), new IssuedToken(Token("a"), new TokenExpiry.NotChecked()), GitHubBaseUrl());

        // Act
        credential.Update(AccountName.Create("my-org").ValueOrThrow(), new IssuedToken(Token("b"), new TokenExpiry.NotChecked()), GitHubBaseUrl());

        // Assert
        credential.DomainEvents.ShouldBeEmpty();
    }
}
