using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Domain;
using Foundry.Modules.Settings.Domain.ValueObjects;
using Foundry.Modules.Settings.Features;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Features.GlobalSettingsMapperTests;

public sealed class ToSummary
{
    private static GlobalSettings CreateDefaultSettings()
    {
        return GlobalSettings.Create();
    }

    [Fact]
    public void WhenDefaultSettings_WorkerImageFlagsAreAllFalse()
    {
        // Arrange
        GlobalSettings settings = CreateDefaultSettings();

        // Act
        GlobalSettingsSummary result = GlobalSettingsMapper.ToSummary(settings);

        // Assert
        result.ShouldSatisfyAllConditions(
            () => result.InstallDotnet.ShouldBeFalse(),
            () => result.InstallAngular.ShouldBeFalse(),
            () => result.InstallGlab.ShouldBeFalse(),
            () => result.InstallGh.ShouldBeFalse());
    }

    [Fact]
    public void WhenInstallDotnetIsTrue_SummaryReflectsIt()
    {
        // Arrange
        GlobalSettings settings = CreateDefaultSettings();
        settings.UpdateWorkerImageConfiguration(new WorkerImageConfiguration(
            InstallDotnet: true,
            InstallAngular: false,
            InstallGlab: false,
            InstallGh: false,
            InstallChromium: false,
            InstallDocker: false));

        // Act
        GlobalSettingsSummary result = GlobalSettingsMapper.ToSummary(settings);

        // Assert
        result.InstallDotnet.ShouldBeTrue();
    }

    [Fact]
    public void WhenAllFlagsEnabled_SummaryReflectsAllFlags()
    {
        // Arrange
        GlobalSettings settings = CreateDefaultSettings();
        settings.UpdateWorkerImageConfiguration(new WorkerImageConfiguration(
            InstallDotnet: true,
            InstallAngular: true,
            InstallGlab: true,
            InstallGh: true,
            InstallChromium: false,
            InstallDocker: false));

        // Act
        GlobalSettingsSummary result = GlobalSettingsMapper.ToSummary(settings);

        // Assert
        result.ShouldSatisfyAllConditions(
            () => result.InstallDotnet.ShouldBeTrue(),
            () => result.InstallAngular.ShouldBeTrue(),
            () => result.InstallGlab.ShouldBeTrue(),
            () => result.InstallGh.ShouldBeTrue());
    }

    [Fact]
    public void WhenDefaultSettings_ImageBuildStatusIsIdle()
    {
        // Arrange
        GlobalSettings settings = CreateDefaultSettings();

        // Act
        GlobalSettingsSummary result = GlobalSettingsMapper.ToSummary(settings);

        // Assert
        result.ImageBuildStatus.ShouldBe(ImageBuildStatus.Idle);
    }

    [Fact]
    public void WhenBuildStarted_ImageBuildStatusIsBuilding()
    {
        // Arrange
        GlobalSettings settings = CreateDefaultSettings();
        settings.BeginImageBuild();

        // Act
        GlobalSettingsSummary result = GlobalSettingsMapper.ToSummary(settings);

        // Assert
        result.ImageBuildStatus.ShouldBe(ImageBuildStatus.Building);
    }

    [Fact]
    public void WhenBuildFailed_ImageBuildStatusIsFailedAndErrorIsSet()
    {
        // Arrange
        GlobalSettings settings = CreateDefaultSettings();
        settings.BeginImageBuild();
        settings.FailImageBuild("Something went wrong");

        // Act
        GlobalSettingsSummary result = GlobalSettingsMapper.ToSummary(settings);

        // Assert
        result.ShouldSatisfyAllConditions(
            () => result.ImageBuildStatus.ShouldBe(ImageBuildStatus.Failed),
            () => result.LastImageBuildError.ShouldBe("Something went wrong"));
    }

    [Fact]
    public void WhenDefaultSettings_LastImageBuildErrorIsNull()
    {
        // Arrange
        GlobalSettings settings = CreateDefaultSettings();

        // Act
        GlobalSettingsSummary result = GlobalSettingsMapper.ToSummary(settings);

        // Assert
        result.LastImageBuildError.ShouldBeNull();
    }

    [Fact]
    public void WhenBuildCompleted_ImageBuildStatusIsIdleAndErrorIsNull()
    {
        // Arrange
        GlobalSettings settings = CreateDefaultSettings();
        settings.BeginImageBuild();
        settings.FailImageBuild("transient error");
        settings.BeginImageBuild();
        settings.CompleteImageBuild();

        // Act
        GlobalSettingsSummary result = GlobalSettingsMapper.ToSummary(settings);

        // Assert
        result.ShouldSatisfyAllConditions(
            () => result.ImageBuildStatus.ShouldBe(ImageBuildStatus.Idle),
            () => result.LastImageBuildError.ShouldBeNull());
    }
}
