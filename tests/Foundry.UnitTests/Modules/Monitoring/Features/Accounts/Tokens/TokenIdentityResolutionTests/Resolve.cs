using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Features.Accounts.Tokens;
using Foundry.Shared;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.Accounts.Tokens.TokenIdentityResolutionTests;

public sealed class Resolve
{
    [Fact]
    public void WhenAuthenticatedWithAllScopes_ReturnsTheAccountName()
    {
        // Arrange
        ValidateToken.Response response = new(ValidateToken.Kinds.Authenticated, "octocat", [], null);

        // Act
        Result<ResolvedTokenIdentity> result = TokenIdentityResolution.Resolve(response);

        // Assert
        result.ValueOrThrow().Name.Value.ShouldBe("octocat");
    }

    [Fact]
    public void WhenScopesUnverifiable_ReturnsTheAccountName()
    {
        // Arrange
        ValidateToken.Response response = new(ValidateToken.Kinds.ScopesUnverifiable, "group-bot", [], null);

        // Act
        Result<ResolvedTokenIdentity> result = TokenIdentityResolution.Resolve(response);

        // Assert
        result.ValueOrThrow().Name.Value.ShouldBe("group-bot");
    }

    [Fact]
    public void WhenAuthenticatedWithMissingScopes_ReturnsInvalidToken()
    {
        // Arrange
        ValidateToken.Response response = new(ValidateToken.Kinds.Authenticated, "octocat", ["repo"], null);

        // Act
        Result<ResolvedTokenIdentity> result = TokenIdentityResolution.Resolve(response);

        // Assert
        ErrorOf(result).ShouldBe(CredentialErrors.InvalidToken);
    }

    [Fact]
    public void WhenAuthenticatedWithoutAnAccountName_ReturnsInvalidToken()
    {
        // Arrange
        ValidateToken.Response response = new(ValidateToken.Kinds.Authenticated, null, [], null);

        // Act
        Result<ResolvedTokenIdentity> result = TokenIdentityResolution.Resolve(response);

        // Assert
        ErrorOf(result).ShouldBe(CredentialErrors.InvalidToken);
    }

    [Fact]
    public void WhenAuthenticationFailed_ReturnsInvalidToken()
    {
        // Arrange
        ValidateToken.Response response = new(ValidateToken.Kinds.AuthenticationFailed, null, [], null);

        // Act
        Result<ResolvedTokenIdentity> result = TokenIdentityResolution.Resolve(response);

        // Assert
        ErrorOf(result).ShouldBe(CredentialErrors.InvalidToken);
    }

    [Fact]
    public void WhenIdentityUnresolved_ReturnsUnresolvedIdentity()
    {
        // Arrange
        ValidateToken.Response response = new(ValidateToken.Kinds.IdentityUnresolved, null, [], null);

        // Act
        Result<ResolvedTokenIdentity> result = TokenIdentityResolution.Resolve(response);

        // Assert
        ErrorOf(result).ShouldBe(CredentialErrors.UnresolvedIdentity);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void WhenScopesUnverifiableWithABlankAccountName_ReturnsUnresolvedIdentity(string? accountName)
    {
        // Arrange
        ValidateToken.Response response = new(ValidateToken.Kinds.ScopesUnverifiable, accountName, [], null);

        // Act
        Result<ResolvedTokenIdentity> result = TokenIdentityResolution.Resolve(response);

        // Assert
        ErrorOf(result).ShouldBe(CredentialErrors.UnresolvedIdentity);
    }

    [Fact]
    public void WhenAuthenticatedWithAnOverlongAccountName_ReturnsUnresolvedIdentity()
    {
        // Arrange
        ValidateToken.Response response = new(ValidateToken.Kinds.Authenticated, new string('a', 201), [], null);

        // Act
        Result<ResolvedTokenIdentity> result = TokenIdentityResolution.Resolve(response);

        // Assert
        ErrorOf(result).ShouldBe(CredentialErrors.UnresolvedIdentity);
    }

    [Fact]
    public void WhenProviderMismatch_ReturnsProviderMismatchNamingTheDetectedProvider()
    {
        // Arrange
        ValidateToken.Response response = new(ValidateToken.Kinds.ProviderMismatch, null, [], "gitlab");

        // Act
        Result<ResolvedTokenIdentity> result = TokenIdentityResolution.Resolve(response);

        // Assert
        ErrorOf(result).ShouldBe(CredentialErrors.ProviderMismatch("gitlab"));
    }

    [Fact]
    public void WhenProviderMismatchWithoutADetectedProvider_ReturnsProviderMismatchWithAnEmptyName()
    {
        // Arrange
        ValidateToken.Response response = new(ValidateToken.Kinds.ProviderMismatch, null, [], null);

        // Act
        Result<ResolvedTokenIdentity> result = TokenIdentityResolution.Resolve(response);

        // Assert
        ErrorOf(result).ShouldBe(CredentialErrors.ProviderMismatch(string.Empty));
    }

    private static Error ErrorOf(Result<ResolvedTokenIdentity> result) =>
        result.ShouldBeOfType<Result<ResolvedTokenIdentity>.Failure>().Error;
}
