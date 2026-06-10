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
        UpdateAuthMode.Command command = new(mode, mode == "api_key" ? "sk-ant-key" : null, null);

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
        UpdateAuthMode.Command command = new(mode, null, null);

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
        UpdateAuthMode.Command command = new("api_key", apiKey, null);

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
        UpdateAuthMode.Command command = new("oauth", null, null);

        // Act
        Result result = _sut.Validate(command);

        // Assert
        result.IsSuccess.ShouldBeTrue();
    }
}
