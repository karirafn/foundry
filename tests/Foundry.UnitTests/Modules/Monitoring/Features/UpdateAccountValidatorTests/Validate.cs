using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Features.Accounts;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.UpdateAccountValidatorTests;

public sealed class Validate
{
    private static readonly AccountId ValidId = AccountId.New();

    private static UpdateAccount.Command ValidCommandWithToken =>
        new(ValidId, "My Account", "https://github.com", "ghp_valid_token");

    private static UpdateAccount.Command ValidCommandNoToken =>
        new(ValidId, "My Account", "https://github.com", null);

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
