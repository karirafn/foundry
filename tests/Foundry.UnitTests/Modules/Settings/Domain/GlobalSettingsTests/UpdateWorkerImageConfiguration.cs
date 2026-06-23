using Foundry.Modules.Settings.Domain;
using Foundry.Modules.Settings.Domain.ValueObjects;

using Shouldly;

using Xunit;

using ImageConfig = Foundry.Modules.Settings.Domain.ValueObjects.WorkerImageConfiguration;

namespace Foundry.UnitTests.Modules.Settings.Domain.GlobalSettingsTests;

public sealed class UpdateWorkerImageConfiguration
{
    [Fact]
    public void WhenConfigurationChanged_ReturnsTrueAndUpdatesConfiguration()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        ImageConfig newConfig = new(
            InstallDotnet: true,
            InstallAngular: false,
            InstallGlab: false,
            InstallGh: false);

        // Act
        bool changed = settings.UpdateWorkerImageConfiguration(newConfig);

        // Assert
        changed.ShouldBeTrue();
        settings.WorkerImageConfiguration.ShouldBe(newConfig);
    }

    [Fact]
    public void WhenConfigurationUnchanged_ReturnsFalseAndDoesNotMutate()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        ImageConfig sameConfig = ImageConfig.Default;

        // Act
        bool changed = settings.UpdateWorkerImageConfiguration(sameConfig);

        // Assert
        changed.ShouldBeFalse();
        settings.WorkerImageConfiguration.ShouldBe(ImageConfig.Default);
    }

    [Fact]
    public void WhenConfigurationChanged_UpdatesUpdatedAt()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        DateTimeOffset before = settings.UpdatedAt;
        ImageConfig newConfig = new(
            InstallDotnet: true,
            InstallAngular: false,
            InstallGlab: false,
            InstallGh: false);

        // Act
        settings.UpdateWorkerImageConfiguration(newConfig);

        // Assert
        settings.UpdatedAt.ShouldBeGreaterThanOrEqualTo(before);
    }

    [Fact]
    public void WhenConfigurationUnchanged_DoesNotUpdateUpdatedAt()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        DateTimeOffset originalUpdatedAt = settings.UpdatedAt;
        ImageConfig sameConfig = ImageConfig.Default;

        // Act
        settings.UpdateWorkerImageConfiguration(sameConfig);

        // Assert
        settings.UpdatedAt.ShouldBe(originalUpdatedAt);
    }
}
