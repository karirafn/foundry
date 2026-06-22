using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Domain;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Domain.GlobalSettingsTests;

public sealed class BeginImageBuild
{
    [Fact]
    public void WhenCalled_SetsStatusToBuilding()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();

        // Act
        settings.BeginImageBuild();

        // Assert
        settings.ImageBuildStatus.ShouldBe(ImageBuildStatus.Building);
    }

    [Fact]
    public void WhenCalled_ClearsLastImageBuildError()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        settings.FailImageBuild("some error");

        // Act
        settings.BeginImageBuild();

        // Assert
        settings.LastImageBuildError.ShouldBeNull();
    }

    [Fact]
    public void WhenCalled_UpdatesUpdatedAt()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        DateTimeOffset before = settings.UpdatedAt;

        // Act
        settings.BeginImageBuild();

        // Assert
        settings.UpdatedAt.ShouldBeGreaterThanOrEqualTo(before);
    }
}
