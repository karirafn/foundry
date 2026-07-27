using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Modules.Settings.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Domain.Entities.GlobalSettingsTests;

public sealed class InitialState
{
    [Fact]
    public void WhenCreated_WorkerImageConfigurationIsDefault()
    {
        // Act
        GlobalSettings settings = GlobalSettings.Create();

        // Assert
        settings.WorkerImageConfiguration.ShouldBe(
            Foundry.Modules.Settings.Domain.ValueObjects.WorkerImageConfiguration.Default);
    }

    [Fact]
    public void WhenCreated_ImageBuildStateIsIdle()
    {
        // Act
        GlobalSettings settings = GlobalSettings.Create();

        // Assert
        settings.ImageBuildState.ShouldBeOfType<ImageBuildState.Idle>();
    }

    [Fact]
    public void WhenCreated_LastImageBuiltAtIsNull()
    {
        // Act
        GlobalSettings settings = GlobalSettings.Create();

        // Assert
        settings.LastImageBuiltAt.ShouldBeNull();
    }
}
