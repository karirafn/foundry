using Foundry.Modules.Monitoring.Infrastructure;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure.TokenValidationResultTests;

public sealed class Factories
{
    [Fact]
    public void ScopesUnverifiable_WhenAccountNameIsProvided_SetsExpectedProperties()
    {
        // Arrange
        const string AccountName = "octocat";

        // Act
        TokenValidationResult result = TokenValidationResult.ScopesUnverifiable(AccountName);

        // Assert
        result.ShouldSatisfyAllConditions(
            () => result.IsValid.ShouldBeFalse(),
            () => result.IsAuthFailure.ShouldBeFalse(),
            () => result.ScopesVerified.ShouldBeFalse(),
            () => result.MissingScopes.ShouldBeEmpty(),
            () => result.AccountName.ShouldBe(AccountName));
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
    public void Validated_WhenNoMissingScopes_SetsExpectedProperties()
    {
        // Arrange

        // Act
        TokenValidationResult result = TokenValidationResult.Validated([], "octocat");

        // Assert
        result.ShouldSatisfyAllConditions(
            () => result.IsValid.ShouldBeTrue(),
            () => result.IsAuthFailure.ShouldBeFalse(),
            () => result.ScopesVerified.ShouldBeTrue(),
            () => result.MissingScopes.ShouldBeEmpty(),
            () => result.AccountName.ShouldBe("octocat"));
    }

    [Fact]
    public void Validated_WhenHasMissingScopes_SetsExpectedProperties()
    {
        // Arrange

        // Act
        TokenValidationResult result = TokenValidationResult.Validated(["repo"], "octocat");

        // Assert
        result.ShouldSatisfyAllConditions(
            () => result.IsValid.ShouldBeFalse(),
            () => result.IsAuthFailure.ShouldBeFalse(),
            () => result.ScopesVerified.ShouldBeTrue(),
            () => result.MissingScopes.ShouldContain("repo"),
            () => result.AccountName.ShouldBe("octocat"));
    }

    [Fact]
    public void AuthFailure_SetsExpectedProperties()
    {
        // Arrange

        // Act
        TokenValidationResult result = TokenValidationResult.AuthFailure();

        // Assert
        result.ShouldSatisfyAllConditions(
            () => result.IsValid.ShouldBeFalse(),
            () => result.IsAuthFailure.ShouldBeTrue(),
            () => result.ScopesVerified.ShouldBeFalse(),
            () => result.MissingScopes.ShouldBeEmpty(),
            () => result.AccountName.ShouldBeNull());
    }
}
