using Foundry.Modules.Settings.Domain;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Domain.GlobalSettingsTests;

public sealed class BeginImageBuild
{
    [Fact]
    public void WhenCalled_SetsStateToBuildingRecord()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();

        // Act
        settings.BeginImageBuild();

        // Assert
        settings.ImageBuildState.ShouldBeOfType<ImageBuildState.Building>();
    }

    [Fact]
    public void WhenCalledAfterFail_StateNoLongerContainsErrorTail()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        settings.FailImageBuild("some error");

        // Act
        settings.BeginImageBuild();

        // Assert
        settings.ImageBuildState.ShouldBeOfType<ImageBuildState.Building>();
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

    [Fact]
    public void WhenCalledAfterSuccessfulBuild_LastImageBuiltAtIsPreserved()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        settings.BeginImageBuild();
        settings.CompleteImageBuild();
        DateTimeOffset? lastBuiltAt = settings.LastImageBuiltAt;

        // Act
        settings.BeginImageBuild();

        // Assert
        settings.LastImageBuiltAt.ShouldBe(lastBuiltAt);
    }
}
