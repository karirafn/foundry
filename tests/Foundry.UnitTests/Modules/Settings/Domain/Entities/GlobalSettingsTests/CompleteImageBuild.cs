using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Modules.Settings.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Domain.Entities.GlobalSettingsTests;

public sealed class CompleteImageBuild
{
    [Fact]
    public void WhenCalled_SetsStateToIdleRecord()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        settings.BeginImageBuild();

        // Act
        settings.CompleteImageBuild();

        // Assert
        settings.ImageBuildState.ShouldBeOfType<ImageBuildState.Idle>();
    }

    [Fact]
    public void WhenCalledAfterFail_StateNoLongerContainsErrorTail()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        settings.FailImageBuild("some error", nextRetryAt: null, attempt: 0);

        // Act
        settings.CompleteImageBuild();

        // Assert
        settings.ImageBuildState.ShouldBeOfType<ImageBuildState.Idle>();
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

    [Fact]
    public void WhenCalled_StampsLastImageBuiltAt()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        settings.BeginImageBuild();
        DateTimeOffset before = DateTimeOffset.UtcNow;

        // Act
        settings.CompleteImageBuild();

        // Assert
        settings.LastImageBuiltAt.ShouldNotBeNull();
        settings.LastImageBuiltAt.Value.ShouldBeGreaterThanOrEqualTo(before);
    }

    [Fact]
    public void WhenCalledTwice_LastImageBuiltAtUpdatesToLatest()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        settings.BeginImageBuild();
        settings.CompleteImageBuild();
        DateTimeOffset firstBuildTime = settings.LastImageBuiltAt!.Value;

        // Act
        settings.BeginImageBuild();
        settings.CompleteImageBuild();

        // Assert
        settings.LastImageBuiltAt.ShouldNotBeNull();
        settings.LastImageBuiltAt.Value.ShouldBeGreaterThanOrEqualTo(firstBuildTime);
    }
}
