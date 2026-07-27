using Foundry.Modules.Settings.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Domain.ValueObjects.WorkerImageConfigurationTests;

public sealed class Default
{
    [Fact]
    public void WhenDefault_AllFlagsAreFalse()
    {
        // Arrange & Act
        WorkerImageConfiguration config = WorkerImageConfiguration.Default;

        // Assert
        config.ShouldSatisfyAllConditions(
            () => config.InstallDotnet.ShouldBeFalse(),
            () => config.InstallAngular.ShouldBeFalse(),
            () => config.InstallGlab.ShouldBeFalse(),
            () => config.InstallGh.ShouldBeFalse(),
            () => config.InstallChromium.ShouldBeFalse(),
            () => config.InstallDocker.ShouldBeFalse());
    }
}
