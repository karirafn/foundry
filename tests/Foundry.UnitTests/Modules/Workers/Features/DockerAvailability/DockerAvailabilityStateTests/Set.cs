using Foundry.Modules.Workers.Features.DockerAvailability;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.DockerAvailability.DockerAvailabilityStateTests;

public sealed class Set
{
    [Fact]
    public void WhenNew_IsAvailableIsFalse()
    {
        // Arrange
        DockerAvailabilityState sut = new();

        // Act
        bool result = ((IDockerAvailabilityState)sut).IsAvailable;

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public void WhenSetTrue_IsAvailableIsTrue()
    {
        // Arrange
        DockerAvailabilityState sut = new();

        // Act
        sut.Set(true);

        // Assert
        ((IDockerAvailabilityState)sut).IsAvailable.ShouldBeTrue();
    }

    [Fact]
    public void WhenSetFalse_IsAvailableIsFalse()
    {
        // Arrange
        DockerAvailabilityState sut = new();
        sut.Set(true);

        // Act
        sut.Set(false);

        // Assert
        ((IDockerAvailabilityState)sut).IsAvailable.ShouldBeFalse();
    }
}
