using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Domain;
using Foundry.Modules.Settings.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Domain.GlobalSettingsTests;

public sealed class WorkerImageConfiguration
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
    public void WhenCreated_ImageBuildStatusIsIdle()
    {
        // Arrange & Act
        GlobalSettings settings = GlobalSettings.Create();

        // Assert
        settings.ImageBuildStatus.ShouldBe(ImageBuildStatus.Idle);
    }

    [Fact]
    public void WhenCreated_LastImageBuildErrorIsNull()
    {
        // Arrange & Act
        GlobalSettings settings = GlobalSettings.Create();

        // Assert
        settings.LastImageBuildError.ShouldBeNull();
    }
}
