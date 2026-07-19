using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Accounts;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.UpdateAccountValidatorTests;

public sealed class Validate
{
    private static readonly CredentialId ValidId = CredentialId.New();

    private static UpdateAccount.Command ValidCommandWithToken =>
        new(ValidId, "https://github.com", "ghp_valid_token");

    private static UpdateAccount.Command ValidCommandNoToken =>
        new(ValidId, "https://github.com", null);

    [Fact]
    public void WhenBaseUrlIsNotHttps_ReturnsBaseUrlInvalidError()
    {
        // Arrange
        UpdateAccount.Validator sut = new();
        UpdateAccount.Command command = ValidCommandWithToken with { BaseUrl = "http://github.com" };

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
        UpdateAccount.Validator sut = new();
        UpdateAccount.Command command = ValidCommandWithToken with { BaseUrl = "https://attacker@github.com" };

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
        UpdateAccount.Validator sut = new();
        string oversizedToken = new('a', UpdateAccount.Validator.TokenMaxLength + 1);
        UpdateAccount.Command command = ValidCommandWithToken with { Token = oversizedToken };

        // Act
        Result result = sut.Validate(command);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Error error = ((Result.Failure)result).Error;
        error.Code.ShouldBe(UpdateAccount.Validator.TokenTooLongCode);
    }

    [Fact]
    public void WhenTokenContainsInvalidCharacters_ReturnsTokenInvalidCharsError()
    {
        // Arrange
        UpdateAccount.Validator sut = new();
        UpdateAccount.Command command = ValidCommandWithToken with { Token = "token with spaces" };

        // Act
        Result result = sut.Validate(command);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Error error = ((Result.Failure)result).Error;
        error.Code.ShouldBe(UpdateAccount.Validator.TokenInvalidCharsCode);
    }

    [Fact]
    public void WhenTokenContainsAtSign_ReturnsTokenInvalidCharsError()
    {
        // Arrange
        UpdateAccount.Validator sut = new();
        UpdateAccount.Command command = ValidCommandWithToken with { Token = "glpat-abc@def" };

        // Act
        Result result = sut.Validate(command);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Error error = ((Result.Failure)result).Error;
        error.Code.ShouldBe(UpdateAccount.Validator.TokenInvalidCharsCode);
    }

    [Fact]
    public void WhenTokenContainsNewline_ReturnsTokenInvalidCharsError()
    {
        // Arrange
        UpdateAccount.Validator sut = new();
        UpdateAccount.Command command = ValidCommandWithToken with { Token = "token\ninjection" };

        // Act
        Result result = sut.Validate(command);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Error error = ((Result.Failure)result).Error;
        error.Code.ShouldBe(UpdateAccount.Validator.TokenInvalidCharsCode);
    }

    [Fact]
    public void WhenTokenIsNull_ReturnsSuccess()
    {
        // Arrange
        UpdateAccount.Validator sut = new();

        // Act
        Result result = sut.Validate(ValidCommandNoToken);

        // Assert
        result.IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData("ghp_validtoken123")]
    [InlineData("glpat-valid_token.123")]
    [InlineData("abc-def_ghi.jkl")]
    [InlineData("UPPER_LOWER-mixed.123")]
    public void WhenTokenContainsOnlyAllowedCharacters_ReturnsSuccess(string token)
    {
        // Arrange
        UpdateAccount.Validator sut = new();
        UpdateAccount.Command command = ValidCommandWithToken with { Token = token };

        // Act
        Result result = sut.Validate(command);

        // Assert
        result.IsSuccess.ShouldBeTrue();
    }
}
