using Foundry.Modules.Monitoring.Infrastructure;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure.TokenValidationResultTests;

public sealed class Factories
{
    [Fact]
    public void ScopesUnverifiable_WhenCalled_IsNotValid()
    {
        // Arrange

        // Act
        TokenValidationResult result = TokenValidationResult.ScopesUnverifiable("octocat");

        // Assert
        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public void ScopesUnverifiable_WhenCalled_IsNotAuthFailure()
    {
        // Arrange

        // Act
        TokenValidationResult result = TokenValidationResult.ScopesUnverifiable("octocat");

        // Assert
        result.IsAuthFailure.ShouldBeFalse();
    }

    [Fact]
    public void ScopesUnverifiable_WhenCalled_ScopesVerifiedIsFalse()
    {
        // Arrange

        // Act
        TokenValidationResult result = TokenValidationResult.ScopesUnverifiable("octocat");

        // Assert
        result.ScopesVerified.ShouldBeFalse();
    }

    [Fact]
    public void ScopesUnverifiable_WhenCalled_MissingScopesIsEmpty()
    {
        // Arrange

        // Act
        TokenValidationResult result = TokenValidationResult.ScopesUnverifiable("octocat");

        // Assert
        result.MissingScopes.ShouldBeEmpty();
    }

    [Fact]
    public void ScopesUnverifiable_WhenCalled_AccountNameIsPreserved()
    {
        // Arrange
        const string AccountName = "octocat";

        // Act
        TokenValidationResult result = TokenValidationResult.ScopesUnverifiable(AccountName);

        // Assert
        result.AccountName.ShouldBe(AccountName);
    }

    [Fact]
    public void ScopesUnverifiable_WhenAccountNameIsNull_AccountNameIsNull()
    {
        // Arrange

        // Act
        TokenValidationResult result = TokenValidationResult.ScopesUnverifiable(null);

        // Assert
        result.AccountName.ShouldBeNull();
    }

    [Fact]
    public void Validated_WhenNoMissingScopes_ScopesVerifiedIsTrue()
    {
        // Arrange

        // Act
        TokenValidationResult result = TokenValidationResult.Validated([], "octocat");

        // Assert
        result.ScopesVerified.ShouldBeTrue();
    }

    [Fact]
    public void Validated_WhenHasMissingScopes_ScopesVerifiedIsTrue()
    {
        // Arrange

        // Act
        TokenValidationResult result = TokenValidationResult.Validated(["repo"], "octocat");

        // Assert
        result.ScopesVerified.ShouldBeTrue();
    }

    [Fact]
    public void AuthFailure_ScopesVerifiedIsFalse()
    {
        // Arrange

        // Act
        TokenValidationResult result = TokenValidationResult.AuthFailure();

        // Assert
        result.ScopesVerified.ShouldBeFalse();
    }
}
