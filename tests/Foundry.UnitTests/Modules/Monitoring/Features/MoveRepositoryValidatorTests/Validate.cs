using Foundry.Modules.Monitoring.Features.Repositories;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.MoveRepositoryValidatorTests;

public sealed class Validate
{
    private static readonly Guid ValidId = Guid.NewGuid();

    [Fact]
    public void WhenPositionIsNegative_ReturnsPositionNegativeError()
    {
        // Arrange
        MoveRepository.Validator sut = new();
        MoveRepository.Command command = new(ValidId, Position: -1);

        // Act
        Result result = sut.Validate(command);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Error error = ((Result.Failure)result).Error;
        error.Code.ShouldBe(MoveRepository.Validator.PositionNegativeCode);
    }

    [Fact]
    public void WhenPositionIsZero_ReturnsSuccess()
    {
        // Arrange
        MoveRepository.Validator sut = new();
        MoveRepository.Command command = new(ValidId, Position: 0);

        // Act
        Result result = sut.Validate(command);

        // Assert
        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void WhenPositionIsPositive_ReturnsSuccess()
    {
        // Arrange
        MoveRepository.Validator sut = new();
        MoveRepository.Command command = new(ValidId, Position: 5);

        // Act
        Result result = sut.Validate(command);

        // Assert
        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void WhenPositionExceedsRepositoryCount_HandlerClampsToLastSlot()
    {
        // Arrange — validator passes; handler clamps at runtime.
        // This test documents that a too-large position is intentionally allowed by the validator
        // (the upper bound is clamped in the handler to accept any non-negative value).
        MoveRepository.Validator sut = new();
        MoveRepository.Command command = new(ValidId, Position: int.MaxValue);

        // Act
        Result result = sut.Validate(command);

        // Assert
        result.IsSuccess.ShouldBeTrue();
    }
}
