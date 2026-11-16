using Foundry.Modules.Settings.Domain.Entities;

using Shouldly;

using Xunit;

using ImageConfig = Foundry.Modules.Settings.Domain.ValueObjects.WorkerImageConfiguration;

namespace Foundry.UnitTests.Modules.Settings.Domain.Entities.GlobalSettingsTests;

public sealed class UpdateNestedDocker
{
    [Fact]
    public void WhenCreated_NestedDockerIsOff()
    {
        // Arrange & Act
        GlobalSettings settings = GlobalSettings.Create();

        // Assert
        settings.NestedDockerEnabled.ShouldBeFalse();
    }

    [Fact]
    public void WhenTurnedOn_ReturnsTrueAndStoresIt()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();

        // Act
        bool changed = settings.UpdateNestedDocker(true);

        // Assert
        changed.ShouldBeTrue();
        settings.NestedDockerEnabled.ShouldBeTrue();
    }

    [Fact]
    public void WhenUnchanged_ReturnsFalseAndDoesNotBumpUpdatedAt()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        DateTimeOffset originalUpdatedAt = settings.UpdatedAt;

        // Act
        bool changed = settings.UpdateNestedDocker(false);

        // Assert
        changed.ShouldBeFalse();
        settings.UpdatedAt.ShouldBe(originalUpdatedAt);
    }

    [Fact]
    public void WhenTurnedOn_LeavesTheWorkerImageConfigurationUnchanged()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();

        // Act
        settings.UpdateNestedDocker(true);

        // Assert
        settings.WorkerImageConfiguration.ShouldBe(ImageConfig.Default);
    }

    [Fact]
    public void WhenTurnedOn_RaisesNoIntegrationEvent()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();

        // Act
        settings.UpdateNestedDocker(true);

        // Assert
        settings.IntegrationEvents.ShouldBeEmpty();
    }
}
