using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Domain;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Domain.GlobalSettingsTests;

public sealed class FailImageBuild
{
    [Fact]
    public void WhenCalled_SetsStatusToFailed()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        settings.BeginImageBuild();

        // Act
        settings.FailImageBuild("error log tail");

        // Assert
        settings.ImageBuildStatus.ShouldBe(ImageBuildStatus.Failed);
    }

    [Fact]
    public void WhenCalled_StoresErrorTail()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        settings.BeginImageBuild();
        const string errorTail = "Step 5/10 : RUN apt-get install dotnet\nERROR: package not found";

        // Act
        settings.FailImageBuild(errorTail);

        // Assert
        settings.LastImageBuildError.ShouldBe(errorTail);
    }

    [Fact]
    public void WhenCalledWithNullErrorTail_StoresNull()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        settings.BeginImageBuild();

        // Act
        settings.FailImageBuild(null);

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
        settings.FailImageBuild("error");

        // Assert
        settings.UpdatedAt.ShouldBeGreaterThanOrEqualTo(before);
    }
}
