using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Accounts;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.CreateAccountValidatorTests;

public sealed class Validate
{
    private static CreateAccount.Command ValidCommand =>
        new("github", "https://github.com", "ghp_valid_token");

    [Fact]
    public void WhenBaseUrlIsNotHttps_ReturnsBaseUrlInvalidError()
    {
        // Arrange
        CreateAccount.Validator sut = new();
        CreateAccount.Command command = ValidCommand with { BaseUrl = "http://github.com" };

        // Act
        Result result = sut.Validate(command);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Error error = ((Result.Failure)result).Error;
        error.Code.ShouldBe(BaseUrlErrors.Invalid.Code);
    }

    [Fact]
    public void WhenBaseUrlContainsCredentials_ReturnsBaseUrlContainsCredentialsError()
    {
        // Arrange
        CreateAccount.Validator sut = new();
        CreateAccount.Command command = ValidCommand with { BaseUrl = "https://attacker@github.com" };

        // Act
        Result result = sut.Validate(command);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Error error = ((Result.Failure)result).Error;
        error.Code.ShouldBe(BaseUrlErrors.ContainsCredentials.Code);
    }

    [Fact]
    public void WhenTokenExceedsMaxLength_ReturnsTokenTooLongError()
    {
        // Arrange
        CreateAccount.Validator sut = new();
        string oversizedToken = new('a', CreateAccount.Validator.TokenMaxLength + 1);
        CreateAccount.Command command = ValidCommand with { Token = oversizedToken };

        // Act
        Result result = sut.Validate(command);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Error error = ((Result.Failure)result).Error;
        error.Code.ShouldBe(CreateAccount.Validator.TokenTooLongCode);
    }

    [Fact]
    public void WhenTokenContainsInvalidCharacters_ReturnsTokenInvalidCharsError()
    {
        // Arrange
        CreateAccount.Validator sut = new();
        CreateAccount.Command command = ValidCommand with { Token = "token with spaces" };

        // Act
        Result result = sut.Validate(command);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Error error = ((Result.Failure)result).Error;
        error.Code.ShouldBe(CreateAccount.Validator.TokenInvalidCharsCode);
    }

    [Fact]
    public void WhenTokenContainsAtSign_ReturnsTokenInvalidCharsError()
    {
        // Arrange
        CreateAccount.Validator sut = new();
        CreateAccount.Command command = ValidCommand with { Token = "glpat-abc@def" };

        // Act
        Result result = sut.Validate(command);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Error error = ((Result.Failure)result).Error;
        error.Code.ShouldBe(CreateAccount.Validator.TokenInvalidCharsCode);
    }

    [Fact]
    public void WhenTokenContainsNewline_ReturnsTokenInvalidCharsError()
    {
        // Arrange
        CreateAccount.Validator sut = new();
        CreateAccount.Command command = ValidCommand with { Token = "token\ninjection" };

        // Act
        Result result = sut.Validate(command);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Error error = ((Result.Failure)result).Error;
        error.Code.ShouldBe(CreateAccount.Validator.TokenInvalidCharsCode);
    }

    [Theory]
    [InlineData("ghp_validtoken123")]
    [InlineData("glpat-valid_token.123")]
    [InlineData("abc-def_ghi.jkl")]
    [InlineData("UPPER_LOWER-mixed.123")]
    public void WhenTokenContainsOnlyAllowedCharacters_ReturnsSuccess(string token)
    {
        // Arrange
        CreateAccount.Validator sut = new();
        CreateAccount.Command command = ValidCommand with { Token = token };

        // Act
        Result result = sut.Validate(command);

        // Assert
        result.IsSuccess.ShouldBeTrue();
    }
}
