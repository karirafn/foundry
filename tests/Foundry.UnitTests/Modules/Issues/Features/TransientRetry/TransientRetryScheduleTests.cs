using Foundry.Modules.Issues.Features.TransientRetry;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Features.TransientRetry;

public sealed class TransientRetryScheduleTests
{
    [Fact]
    public void WhenAttemptIsZero_ComputeBackoffReturnsInitialBackoff()
    {
        // Arrange
        TimeSpan expected = TransientRetrySchedule.InitialBackoff;

        // Act
        TimeSpan result = TransientRetrySchedule.ComputeBackoff(attempt: 0);

        // Assert
        result.ShouldBe(expected);
    }

    [Fact]
    public void WhenAttemptIsOne_ComputeBackoffReturnsInitialBackoff()
    {
        // Arrange
        TimeSpan expected = TransientRetrySchedule.InitialBackoff;

        // Act
        TimeSpan result = TransientRetrySchedule.ComputeBackoff(attempt: 1);

        // Assert
        result.ShouldBe(expected);
    }

    [Fact]
    public void WhenAttemptIsTwo_ComputeBackoffDoublesInitialBackoff()
    {
        // Arrange
        TimeSpan expected = TransientRetrySchedule.InitialBackoff * 2;

        // Act
        TimeSpan result = TransientRetrySchedule.ComputeBackoff(attempt: 2);

        // Assert
        result.ShouldBe(expected);
    }

    [Fact]
    public void WhenAttemptIsThree_ComputeBackoffQuadruplesInitialBackoff()
    {
        // Arrange
        TimeSpan expected = TransientRetrySchedule.InitialBackoff * 4;

        // Act
        TimeSpan result = TransientRetrySchedule.ComputeBackoff(attempt: 3);

        // Assert
        result.ShouldBe(expected);
    }

    [Fact]
    public void TransientApiErrorCategory_IsCorrectLiteralValue()
    {
        // Arrange / Act / Assert
        TransientRetrySchedule.TransientApiErrorCategory.ShouldBe("transient_api_error");
    }

    [Fact]
    public void MaxTransientRetries_IsTwo()
    {
        // Arrange / Act / Assert
        TransientRetrySchedule.MaxTransientRetries.ShouldBe(2);
    }
}
