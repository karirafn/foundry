using Foundry.Modules.Settings.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Domain.ValueObjects.WorkerImageConfigurationTests;

public sealed class ValueEquality
{
    [Fact]
    public void WhenSameValues_AreEqual()
    {
        // Arrange
        WorkerImageConfiguration first = new(
            InstallDotnet: true,
            InstallAngular: false,
            InstallChromium: false,
            InstallDocker: false);

        WorkerImageConfiguration second = new(
            InstallDotnet: true,
            InstallAngular: false,
            InstallChromium: false,
            InstallDocker: false);

        // Act
        bool result = first == second;

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public void WhenDifferentValues_AreNotEqual()
    {
        // Arrange
        WorkerImageConfiguration first = new(
            InstallDotnet: true,
            InstallAngular: false,
            InstallChromium: false,
            InstallDocker: false);

        WorkerImageConfiguration second = new(
            InstallDotnet: false,
            InstallAngular: false,
            InstallChromium: false,
            InstallDocker: false);

        // Act
        bool result = first == second;

        // Assert
        result.ShouldBeFalse();
    }
}
