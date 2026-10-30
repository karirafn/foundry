using Foundry.Modules.Settings.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Domain.ValueObjects.WorkerImageConfigurationTests;

public sealed class ToBuildArgs
{
    private static WorkerImageConfiguration Config(
        bool dotnet = false,
        bool angular = false,
        bool chromium = false,
        bool docker = false) =>
        WorkerImageConfiguration.Create(
            imageTag: "foundry-worker:local",
            buildEnabled: true,
            installDotnet: dotnet,
            installAngular: angular,
            installChromium: chromium,
            installDocker: docker)
            .Match(v => v, e => throw new InvalidOperationException(e.Message));

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
        WorkerImageConfiguration config = Config(dotnet: true);

        // Act
        IReadOnlyDictionary<string, string> args = config.ToBuildArgs();

        // Assert
        args["INSTALL_DOTNET"].ShouldBe("true");
    }

    [Fact]
    public void WhenInstallAngularIsTrue_BuildArgIsTrue()
    {
        // Arrange
        WorkerImageConfiguration config = Config(angular: true);

        // Act
        IReadOnlyDictionary<string, string> args = config.ToBuildArgs();

        // Assert
        args["INSTALL_ANGULAR"].ShouldBe("true");
    }

    [Fact]
    public void WhenInstallChromiumIsTrue_BuildArgIsTrue()
    {
        // Arrange
        WorkerImageConfiguration config = Config(chromium: true);

        // Act
        IReadOnlyDictionary<string, string> args = config.ToBuildArgs();

        // Assert
        args["INSTALL_CHROMIUM"].ShouldBe("true");
    }

    [Fact]
    public void WhenInstallDockerIsTrue_BuildArgIsTrue()
    {
        // Arrange
        WorkerImageConfiguration config = Config(docker: true);

        // Act
        IReadOnlyDictionary<string, string> args = config.ToBuildArgs();

        // Assert
        args["INSTALL_DOCKER"].ShouldBe("true");
    }

    [Fact]
    public void WhenAllFlagsAreTrue_AllBuildArgsAreTrue()
    {
        // Arrange
        WorkerImageConfiguration config = Config(dotnet: true, angular: true, chromium: true, docker: true);

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

    [Fact]
    public void WhenCalled_DoesNotIncludeImageTagEntry()
    {
        // Arrange
        WorkerImageConfiguration config = WorkerImageConfiguration.Default;

        // Act
        IReadOnlyDictionary<string, string> args = config.ToBuildArgs();

        // Assert
        args.ContainsKey("IMAGE_TAG").ShouldBeFalse();
    }

    [Fact]
    public void WhenCalled_DoesNotIncludeBuildEnabledEntry()
    {
        // Arrange
        WorkerImageConfiguration config = WorkerImageConfiguration.Default;

        // Act
        IReadOnlyDictionary<string, string> args = config.ToBuildArgs();

        // Assert
        args.ContainsKey("BUILD_ENABLED").ShouldBeFalse();
    }
}
