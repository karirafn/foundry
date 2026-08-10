using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Domain.ValueObjects;
using Foundry.Modules.Settings.Features;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Features.ImageBuildStatusMapperTests;

public sealed class ToStatus
{
    [Fact]
    public void WhenStateIsBuilding_ReturnsBuilding()
    {
        // Arrange
        ImageBuildState state = new ImageBuildState.Building();

        // Act
        ImageBuildStatus result = state.ToStatus();

        // Assert
        result.ShouldBe(ImageBuildStatus.Building);
    }

    [Fact]
    public void WhenStateIsFailed_ReturnsFailed()
    {
        // Arrange
        ImageBuildState state = new ImageBuildState.Failed(ErrorTail: "some error", NextRetryAt: null, Attempt: 0);

        // Act
        ImageBuildStatus result = state.ToStatus();

        // Assert
        result.ShouldBe(ImageBuildStatus.Failed);
    }

    [Fact]
    public void WhenStateIsIdle_ReturnsIdle()
    {
        // Arrange
        ImageBuildState state = new ImageBuildState.Idle();

        // Act
        ImageBuildStatus result = state.ToStatus();

        // Assert
        result.ShouldBe(ImageBuildStatus.Idle);
    }
}
