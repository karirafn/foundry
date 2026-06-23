using Foundry.Modules.Settings.Domain;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Domain.GlobalSettingsTests;

public sealed class InitialState
{
    [Fact]
    public void WhenCreated_WorkerImageConfigurationIsDefault()
    {
        // Arrange & Act
        GlobalSettings settings = GlobalSettings.Create();

        // Assert
        settings.WorkerImageConfiguration.ShouldBe(
            Foundry.Modules.Settings.Domain.ValueObjects.WorkerImageConfiguration.Default);
    }

    [Fact]
    public void WhenCreated_ImageBuildStateIsIdle()
    {
        // Arrange & Act
        GlobalSettings settings = GlobalSettings.Create();

        // Assert
        settings.ImageBuildState.ShouldBeOfType<ImageBuildState.Idle>();
    }
}
