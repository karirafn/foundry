using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Domain;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Domain.GlobalSettingsTests;

public sealed class CompleteImageBuild
{
    [Fact]
    public void WhenCalled_SetsStatusToIdle()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        settings.BeginImageBuild();

        // Act
        settings.CompleteImageBuild();

        // Assert
        settings.ImageBuildStatus.ShouldBe(ImageBuildStatus.Idle);
    }

    [Fact]
    public void WhenCalled_ClearsLastImageBuildError()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        settings.FailImageBuild("some error");

        // Act
        settings.CompleteImageBuild();

        // Assert
        settings.LastImageBuildError.ShouldBeNull();
    }

    [Fact]
    public void WhenCalled_UpdatesUpdatedAt()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        settings.BeginImageBuild();
        DateTimeOffset before = settings.UpdatedAt;

        // Act
        settings.CompleteImageBuild();

        // Assert
        settings.UpdatedAt.ShouldBeGreaterThanOrEqualTo(before);
    }
}
