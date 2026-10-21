using Foundry.Modules.Settings.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Domain.ValueObjects.WorkerImageConfigurationTests;

public sealed class ToBuildArgs
{
    [Fact]
    public void WhenAllFlagsAreFalse_AllBuildArgsAreFalse()
    {
        // Arrange
        WorkerImageConfiguration config = WorkerImageConfiguration.Default;

        // Act
        IReadOnlyDictionary<string, string> args = config.ToBuildArgs();

        // Assert
        args.ShouldSatisfyAllConditions(
            () => args["INSTALL_DOTNET"].ShouldBe("false"),
            () => args["INSTALL_ANGULAR"].ShouldBe("false"),
            () => args["INSTALL_CHROMIUM"].ShouldBe("false"),
            () => args["INSTALL_DOCKER"].ShouldBe("false"));
    }

    [Fact]
    public void WhenInstallDotnetIsTrue_BuildArgIsTrue()
    {
        // Arrange
        WorkerImageConfiguration config = new(
            InstallDotnet: true,
            InstallAngular: false,
            InstallChromium: false,
            InstallDocker: false);

        // Act
        IReadOnlyDictionary<string, string> args = config.ToBuildArgs();

        // Assert
        args["INSTALL_DOTNET"].ShouldBe("true");
    }

    [Fact]
    public void WhenInstallAngularIsTrue_BuildArgIsTrue()
    {
        // Arrange
        WorkerImageConfiguration config = new(
            InstallDotnet: false,
            InstallAngular: true,
            InstallChromium: false,
            InstallDocker: false);

        // Act
        IReadOnlyDictionary<string, string> args = config.ToBuildArgs();

        // Assert
        args["INSTALL_ANGULAR"].ShouldBe("true");
    }

    [Fact]
    public void WhenInstallChromiumIsTrue_BuildArgIsTrue()
    {
        // Arrange
        WorkerImageConfiguration config = new(
            InstallDotnet: false,
            InstallAngular: false,
            InstallChromium: true,
            InstallDocker: false);

        // Act
        IReadOnlyDictionary<string, string> args = config.ToBuildArgs();

        // Assert
        args["INSTALL_CHROMIUM"].ShouldBe("true");
    }

    [Fact]
    public void WhenInstallDockerIsTrue_BuildArgIsTrue()
    {
        // Arrange
        WorkerImageConfiguration config = new(
            InstallDotnet: false,
            InstallAngular: false,
            InstallChromium: false,
            InstallDocker: true);

        // Act
        IReadOnlyDictionary<string, string> args = config.ToBuildArgs();

        // Assert
        args["INSTALL_DOCKER"].ShouldBe("true");
    }

    [Fact]
    public void WhenAllFlagsAreTrue_AllBuildArgsAreTrue()
    {
        // Arrange
        WorkerImageConfiguration config = new(
            InstallDotnet: true,
            InstallAngular: true,
            InstallChromium: true,
            InstallDocker: true);

        // Act
        IReadOnlyDictionary<string, string> args = config.ToBuildArgs();

        // Assert
        args.ShouldSatisfyAllConditions(
            () => args["INSTALL_DOTNET"].ShouldBe("true"),
            () => args["INSTALL_ANGULAR"].ShouldBe("true"),
            () => args["INSTALL_CHROMIUM"].ShouldBe("true"),
            () => args["INSTALL_DOCKER"].ShouldBe("true"));
    }

    [Fact]
    public void WhenCalled_ReturnsFourEntries()
    {
        // Arrange
        WorkerImageConfiguration config = WorkerImageConfiguration.Default;

        // Act
        IReadOnlyDictionary<string, string> args = config.ToBuildArgs();

        // Assert
        args.Count.ShouldBe(4);
    }
}
