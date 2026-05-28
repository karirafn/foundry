using Foundry.WebApi.Modules.Workers.Features;

using Microsoft.Extensions.Options;

using Shouldly;

using Xunit;

namespace Foundry.WebApi.UnitTests.Modules.Workers.Features.WorkerOptionsValidatorTests;

public sealed class Validate
{
    private readonly WorkerOptionsValidator _sut = new();

    [Fact]
    public void WhenApiKeyIsEmpty_ReturnsFailure()
    {
        // Arrange
        WorkerOptions options = new() { ApiKey = string.Empty };

        // Act
        ValidateOptionsResult result = _sut.Validate(null, options);

        // Assert
        result.Failed.ShouldBeTrue();
    }

    [Fact]
    public void WhenApiKeyIsWhitespace_ReturnsFailure()
    {
        // Arrange
        WorkerOptions options = new() { ApiKey = "   " };

        // Act
        ValidateOptionsResult result = _sut.Validate(null, options);

        // Assert
        result.Failed.ShouldBeTrue();
    }

    [Fact]
    public void WhenApiKeyIsNonEmpty_ReturnsSuccess()
    {
        // Arrange
        WorkerOptions options = new() { ApiKey = "sk-ant-api-key" };

        // Act
        ValidateOptionsResult result = _sut.Validate(null, options);

        // Assert
        result.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void WhenApiKeyIsEmpty_FailureMessageMentionsApiKey()
    {
        // Arrange
        WorkerOptions options = new() { ApiKey = string.Empty };

        // Act
        ValidateOptionsResult result = _sut.Validate(null, options);

        // Assert
        string failureMessage = result.Failures.ShouldHaveSingleItem();
        failureMessage.ShouldContain("ApiKey");
    }
}
