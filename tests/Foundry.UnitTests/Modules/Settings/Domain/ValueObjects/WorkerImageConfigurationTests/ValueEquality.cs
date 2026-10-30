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
        WorkerImageConfiguration first = WorkerImageConfiguration.Create(
            imageTag: "foundry-worker:v1",
            buildEnabled: true,
            installDotnet: true,
            installAngular: false,
            installChromium: false,
            installDocker: false)
            .Match(v => v, e => throw new InvalidOperationException(e.Message));

        WorkerImageConfiguration second = WorkerImageConfiguration.Create(
            imageTag: "foundry-worker:v1",
            buildEnabled: true,
            installDotnet: true,
            installAngular: false,
            installChromium: false,
            installDocker: false)
            .Match(v => v, e => throw new InvalidOperationException(e.Message));

        // Act
        bool result = first == second;

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public void WhenDifferentInstallFlags_AreNotEqual()
    {
        // Arrange
        WorkerImageConfiguration first = WorkerImageConfiguration.Create(
            imageTag: "foundry-worker:v1",
            buildEnabled: true,
            installDotnet: true,
            installAngular: false,
            installChromium: false,
            installDocker: false)
            .Match(v => v, e => throw new InvalidOperationException(e.Message));

        WorkerImageConfiguration second = WorkerImageConfiguration.Create(
            imageTag: "foundry-worker:v1",
            buildEnabled: true,
            installDotnet: false,
            installAngular: false,
            installChromium: false,
            installDocker: false)
            .Match(v => v, e => throw new InvalidOperationException(e.Message));

        // Act
        bool result = first == second;

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public void WhenDifferentImageTag_AreNotEqual()
    {
        // Arrange
        WorkerImageConfiguration first = WorkerImageConfiguration.Create(
            imageTag: "foundry-worker:v1",
            buildEnabled: true,
            installDotnet: false,
            installAngular: false,
            installChromium: false,
            installDocker: false)
            .Match(v => v, e => throw new InvalidOperationException(e.Message));

        WorkerImageConfiguration second = WorkerImageConfiguration.Create(
            imageTag: "foundry-worker:v2",
            buildEnabled: true,
            installDotnet: false,
            installAngular: false,
            installChromium: false,
            installDocker: false)
            .Match(v => v, e => throw new InvalidOperationException(e.Message));

        // Act
        bool result = first == second;

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public void WhenDifferentBuildEnabled_AreNotEqual()
    {
        // Arrange
        WorkerImageConfiguration first = WorkerImageConfiguration.Create(
            imageTag: "foundry-worker:v1",
            buildEnabled: true,
            installDotnet: false,
            installAngular: false,
            installChromium: false,
            installDocker: false)
            .Match(v => v, e => throw new InvalidOperationException(e.Message));

        WorkerImageConfiguration second = WorkerImageConfiguration.Create(
            imageTag: "foundry-worker:v1",
            buildEnabled: false,
            installDotnet: false,
            installAngular: false,
            installChromium: false,
            installDocker: false)
            .Match(v => v, e => throw new InvalidOperationException(e.Message));

        // Act
        bool result = first == second;

        // Assert
        result.ShouldBeFalse();
    }
}
