using Foundry.Modules.Settings.Domain;
using Foundry.Modules.Settings.Features;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Features.UpdateAuthModeTests;

public sealed class ValidateCommand
{
    private readonly UpdateAuthMode.Validator _sut = new();

    [Theory]
    [InlineData("api_key")]
    [InlineData("oauth")]
    public void WhenModeIsValid_ReturnsSuccess(string mode)
    {
        // Arrange
        UpdateAuthMode.Command command = new(mode, mode == "api_key" ? "sk-ant-key" : null);

        // Act
        Result result = _sut.Validate(command);

        // Assert
        result.IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("invalid_mode")]
    [InlineData("API_KEY")]
    public void WhenModeIsInvalid_ReturnsInvalidAuthModeError(string mode)
    {
        // Arrange
        UpdateAuthMode.Command command = new(mode, null);

        // Act
        Result result = _sut.Validate(command);

        // Assert
        Result.Failure failure = result.ShouldBeOfType<Result.Failure>();
        failure.Error.Code.ShouldBe(SettingsErrors.InvalidAuthModeCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(null)]
    public void WhenModeIsApiKeyAndKeyIsNullOrWhiteSpace_ReturnsInvalidAuthModeError(string? apiKey)
    {
        // Arrange
        UpdateAuthMode.Command command = new("api_key", apiKey);

        // Act
        Result result = _sut.Validate(command);

        // Assert
        Result.Failure failure = result.ShouldBeOfType<Result.Failure>();
        failure.Error.Code.ShouldBe(SettingsErrors.InvalidAuthModeCode);
    }

    [Fact]
    public void WhenModeIsOAuthAndKeyIsProvided_ReturnsSuccess()
    {
        // Arrange
        // For oauth mode, apiKey is not required
        UpdateAuthMode.Command command = new("oauth", null);

        // Act
        Result result = _sut.Validate(command);

        // Assert
        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void WhenApiKeyExceeds256Characters_ReturnsInvalidAuthModeError()
    {
        // Arrange
        string oversizedKey = new('a', 257);
        UpdateAuthMode.Command command = new("api_key", oversizedKey);

        // Act
        Result result = _sut.Validate(command);

        // Assert
        Result.Failure failure = result.ShouldBeOfType<Result.Failure>();
        failure.Error.Code.ShouldBe(SettingsErrors.InvalidAuthModeCode);
    }

    [Fact]
    public void WhenApiKeyIsExactly256Characters_ReturnsSuccess()
    {
        // Arrange
        string maxLengthKey = new('a', 256);
        UpdateAuthMode.Command command = new("api_key", maxLengthKey);

        // Act
        Result result = _sut.Validate(command);

        // Assert
        result.IsSuccess.ShouldBeTrue();
    }
}
