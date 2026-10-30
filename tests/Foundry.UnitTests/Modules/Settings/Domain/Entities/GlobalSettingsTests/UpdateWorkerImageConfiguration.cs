using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Modules.Settings.Domain.ValueObjects;

using Shouldly;

using Xunit;

using ImageConfig = Foundry.Modules.Settings.Domain.ValueObjects.WorkerImageConfiguration;

namespace Foundry.UnitTests.Modules.Settings.Domain.Entities.GlobalSettingsTests;

public sealed class UpdateWorkerImageConfiguration
{
    private static ImageConfig CreateConfig(
        bool dotnet = false,
        bool angular = false,
        bool chromium = false,
        bool docker = false) =>
        ImageConfig.Create(
            imageTag: "foundry-worker:local",
            buildEnabled: true,
            installDotnet: dotnet,
            installAngular: angular,
            installChromium: chromium,
            installDocker: docker)
            .Match(v => v, e => throw new InvalidOperationException(e.Message));

    [Fact]
    public void WhenConfigurationChanged_ReturnsTrueAndUpdatesConfiguration()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        ImageConfig newConfig = CreateConfig(dotnet: true);

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
        ImageConfig newConfig = CreateConfig(dotnet: true);

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
