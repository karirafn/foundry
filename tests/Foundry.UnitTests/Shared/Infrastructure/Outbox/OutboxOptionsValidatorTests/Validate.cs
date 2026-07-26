using Foundry.Shared.Infrastructure.Outbox;

using Microsoft.Extensions.Options;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Shared.Infrastructure.Outbox.OutboxOptionsValidatorTests;

public sealed class Validate
{
    private readonly OutboxOptionsValidator _sut = new();

    [Fact]
    public void WhenBatchSizeIsZero_ReturnsFailure()
    {
        // Arrange
        OutboxOptions options = new() { BatchSize = 0 };

        // Act
        ValidateOptionsResult result = _sut.Validate(null, options);

        // Assert
        result.Failed.ShouldBeTrue();
    }

    [Fact]
    public void WhenBatchSizeIsNegative_ReturnsFailure()
    {
        // Arrange
        OutboxOptions options = new() { BatchSize = -1 };

        // Act
        ValidateOptionsResult result = _sut.Validate(null, options);

        // Assert
        result.Failed.ShouldBeTrue();
    }

    [Fact]
    public void WhenMaxAttemptsIsZero_ReturnsFailure()
    {
        // Arrange
        OutboxOptions options = new() { MaxAttempts = 0 };

        // Act
        ValidateOptionsResult result = _sut.Validate(null, options);

        // Assert
        result.Failed.ShouldBeTrue();
    }

    [Fact]
    public void WhenMaxAttemptsIsNegative_ReturnsFailure()
    {
        // Arrange
        OutboxOptions options = new() { MaxAttempts = -1 };

        // Act
        ValidateOptionsResult result = _sut.Validate(null, options);

        // Assert
        result.Failed.ShouldBeTrue();
    }

    [Fact]
    public void WhenTickIntervalIsZero_ReturnsFailure()
    {
        // Arrange
        OutboxOptions options = new() { TickInterval = TimeSpan.Zero };

        // Act
        ValidateOptionsResult result = _sut.Validate(null, options);

        // Assert
        result.Failed.ShouldBeTrue();
    }

    [Fact]
    public void WhenRetentionWindowIsZero_ReturnsFailure()
    {
        // Arrange
        OutboxOptions options = new() { RetentionWindow = TimeSpan.Zero };

        // Act
        ValidateOptionsResult result = _sut.Validate(null, options);

        // Assert
        result.Failed.ShouldBeTrue();
    }

    [Fact]
    public void WhenDefaultOptions_ReturnsSuccess()
    {
        // Arrange
        OutboxOptions options = new();

        // Act
        ValidateOptionsResult result = _sut.Validate(null, options);

        // Assert
        result.Succeeded.ShouldBeTrue();
    }
}
