using Foundry.Modules.Workers.Features.ImageBuild;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.ImageBuild;

public sealed class ImageBuildRetryPolicyTests
{
    private static readonly TimeSpan InitialBackoff = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(15);

    private static ImageBuildRetryPolicy CreatePolicy(
        TimeSpan? initialBackoff = null,
        TimeSpan? maxBackoff = null)
        => new(initialBackoff ?? InitialBackoff, maxBackoff ?? MaxBackoff);

    [Fact]
    public void WhenAttemptIsOne_ReturnsInitialBackoff()
    {
        // Arrange
        ImageBuildRetryPolicy policy = CreatePolicy();

        // Act
        TimeSpan result = policy.ComputeBackoff(1);

        // Assert
        result.ShouldBe(InitialBackoff);
    }

    [Fact]
    public void WhenAttemptIsTwo_ReturnsDoubleInitialBackoff()
    {
        // Arrange
        ImageBuildRetryPolicy policy = CreatePolicy();

        // Act
        TimeSpan result = policy.ComputeBackoff(2);

        // Assert
        result.ShouldBe(InitialBackoff * 2);
    }

    [Fact]
    public void WhenAttemptIsThree_ReturnsFourTimesInitialBackoff()
    {
        // Arrange
        ImageBuildRetryPolicy policy = CreatePolicy();

        // Act
        TimeSpan result = policy.ComputeBackoff(3);

        // Assert
        result.ShouldBe(InitialBackoff * 4);
    }

    [Fact]
    public void WhenExponentialExceedsMax_ReturnsCappedAtMax()
    {
        // Arrange — attempt 10 with 30s initial gives 30 * 2^9 = 15360s, well beyond 15min cap
        ImageBuildRetryPolicy policy = CreatePolicy();

        // Act
        TimeSpan result = policy.ComputeBackoff(10);

        // Assert
        result.ShouldBe(MaxBackoff);
    }

    [Fact]
    public void WhenExponentialEqualsMax_ReturnsCappedAtMax()
    {
        // Arrange — exact boundary: initialBackoff=1min, maxBackoff=2min, attempt=2
        // 1min * 2^1 = 2min — equals MaxBackoff, strict < is false, so result must be MaxBackoff
        TimeSpan initialBackoff = TimeSpan.FromMinutes(1);
        TimeSpan maxBackoff = TimeSpan.FromMinutes(2);
        ImageBuildRetryPolicy policy = CreatePolicy(initialBackoff, maxBackoff);

        // Act
        TimeSpan result = policy.ComputeBackoff(2);

        // Assert — the strict < comparison returns max when uncapped == max
        result.ShouldBe(maxBackoff);
    }

    [Fact]
    public void WhenAttemptIsVeryLarge_ReturnsCappedAtMaxWithoutOverflow()
    {
        // Arrange — attempt 1000 would overflow double without the capping; ensure no exception
        ImageBuildRetryPolicy policy = CreatePolicy();

        // Act
        TimeSpan result = policy.ComputeBackoff(1000);

        // Assert
        result.ShouldBe(MaxBackoff);
    }

    [Fact]
    public void WhenAttemptIsZero_ClampsToOneAndReturnsInitialBackoff()
    {
        // Arrange
        ImageBuildRetryPolicy policy = CreatePolicy();

        // Act
        TimeSpan result = policy.ComputeBackoff(0);

        // Assert — zero is clamped to 1, so initial backoff is returned
        result.ShouldBe(InitialBackoff);
    }

    [Fact]
    public void WhenAttemptIsNegative_ClampsToOneAndReturnsInitialBackoff()
    {
        // Arrange
        ImageBuildRetryPolicy policy = CreatePolicy();

        // Act
        TimeSpan result = policy.ComputeBackoff(-5);

        // Assert — negative is clamped to 1, so initial backoff is returned
        result.ShouldBe(InitialBackoff);
    }
}
