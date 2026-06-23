using Foundry.Modules.Settings.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Domain.WorkerImageConfigurationTests;

public sealed class ValueEquality
{
    [Fact]
    public void WhenSameValues_AreEqual()
    {
        // Arrange
        WorkerImageConfiguration first = new(
            InstallDotnet: true,
            InstallAngular: false,
            InstallGlab: true,
            InstallGh: false,
            InstallChromium: false,
            InstallDocker: false);

        WorkerImageConfiguration second = new(
            InstallDotnet: true,
            InstallAngular: false,
            InstallGlab: true,
            InstallGh: false,
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
            InstallGlab: false,
            InstallGh: false,
            InstallChromium: false,
            InstallDocker: false);

        WorkerImageConfiguration second = new(
            InstallDotnet: false,
            InstallAngular: false,
            InstallGlab: false,
            InstallGh: false,
            InstallChromium: false,
            InstallDocker: false);

        // Act
        bool result = first == second;

        // Assert
        result.ShouldBeFalse();
    }
}
