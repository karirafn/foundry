using Foundry.Modules.Settings.Domain.ValueObjects;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Domain.ValueObjects.WorkerImageConfigurationTests;

public sealed class Default
{
    [Fact]
    public void WhenDefault_AllInstallFlagsAreFalse()
    {
        // Arrange & Act
        WorkerImageConfiguration config = WorkerImageConfiguration.Default;

        // Assert
        config.ShouldSatisfyAllConditions(
            () => config.InstallDotnet.ShouldBeFalse(),
            () => config.InstallAngular.ShouldBeFalse(),
            () => config.InstallChromium.ShouldBeFalse(),
            () => config.InstallDocker.ShouldBeFalse());
    }

    [Fact]
    public void WhenDefault_ImageTagIsFoundryWorkerLocal()
    {
        // Arrange & Act
        WorkerImageConfiguration config = WorkerImageConfiguration.Default;

        // Assert
        config.ImageTag.ShouldBe("foundry-worker:local");
    }

    [Fact]
    public void WhenDefault_BuildEnabledIsTrue()
    {
        // Arrange & Act
        WorkerImageConfiguration config = WorkerImageConfiguration.Default;

        // Assert
        config.BuildEnabled.ShouldBeTrue();
    }

    [Fact]
    public void WhenDefault_PassesCreateInvariants()
    {
        // Arrange
        WorkerImageConfiguration defaults = WorkerImageConfiguration.Default;

        // Act
        Result<WorkerImageConfiguration> result = WorkerImageConfiguration.Create(
            defaults.ImageTag,
            defaults.BuildEnabled,
            defaults.InstallDotnet,
            defaults.InstallAngular,
            defaults.InstallChromium,
            defaults.InstallDocker);

        // Assert
        result.IsSuccess.ShouldBeTrue();
    }
}
