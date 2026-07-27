using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Modules.Settings.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Domain.Entities.GlobalSettingsTests;

public sealed class FailImageBuild
{
    [Fact]
    public void WhenCalled_SetsStateToFailedRecord()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        settings.BeginImageBuild();

        // Act
        settings.FailImageBuild("error log tail");

        // Assert
        settings.ImageBuildState.ShouldBeOfType<ImageBuildState.Failed>();
    }

    [Fact]
    public void WhenCalled_StoresErrorTailOnFailedState()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        settings.BeginImageBuild();
        const string errorTail = "Step 5/10 : RUN apt-get install dotnet\nERROR: package not found";

        // Act
        settings.FailImageBuild(errorTail);

        // Assert
        ImageBuildState.Failed failed = settings.ImageBuildState.ShouldBeOfType<ImageBuildState.Failed>();
        failed.ErrorTail.ShouldBe(errorTail);
    }

    [Fact]
    public void WhenCalledWithNullErrorTail_ErrorTailIsNull()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        settings.BeginImageBuild();

        // Act
        settings.FailImageBuild(null);

        // Assert
        ImageBuildState.Failed failed = settings.ImageBuildState.ShouldBeOfType<ImageBuildState.Failed>();
        failed.ErrorTail.ShouldBeNull();
    }

    [Fact]
    public void WhenCalled_UpdatesUpdatedAt()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        settings.BeginImageBuild();
        DateTimeOffset before = settings.UpdatedAt;

        // Act
        settings.FailImageBuild("error");

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
        settings.FailImageBuild("error after prior success");

        // Assert
        settings.LastImageBuiltAt.ShouldBe(lastBuiltAt);
    }
}
