using Foundry.Modules.Settings.Domain.ValueObjects;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Domain.ValueObjects.WorkerImageConfigurationTests;

public sealed class Create
{
    private const string ValidTag = "foundry-worker:v1.0";

    [Fact]
    public void WhenAllInputsAreValid_ReturnsSuccess()
    {
        // Arrange & Act
        Result<WorkerImageConfiguration> result = WorkerImageConfiguration.Create(
            imageTag: ValidTag,
            buildEnabled: true,
            installDotnet: false,
            installAngular: false,
            installChromium: false,
            installDocker: false);

        // Assert
        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void WhenAllInputsAreValid_ReturnsValueWithCorrectFields()
    {
        // Arrange & Act
        Result<WorkerImageConfiguration> result = WorkerImageConfiguration.Create(
            imageTag: ValidTag,
            buildEnabled: true,
            installDotnet: true,
            installAngular: false,
            installChromium: false,
            installDocker: false);

        // Assert
        WorkerImageConfiguration config = result.ShouldBeOfType<Result<WorkerImageConfiguration>.Success>().Value;
        config.ShouldSatisfyAllConditions(
            () => config.ImageTag.ShouldBe(ValidTag),
            () => config.BuildEnabled.ShouldBeTrue(),
            () => config.InstallDotnet.ShouldBeTrue(),
            () => config.InstallAngular.ShouldBeFalse(),
            () => config.InstallChromium.ShouldBeFalse(),
            () => config.InstallDocker.ShouldBeFalse());
    }

    [Fact]
    public void WhenImageTagIsEmpty_ReturnsFailure()
    {
        // Arrange & Act
        Result<WorkerImageConfiguration> result = WorkerImageConfiguration.Create(
            imageTag: string.Empty,
            buildEnabled: true,
            installDotnet: false,
            installAngular: false,
            installChromium: false,
            installDocker: false);

        // Assert
        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void WhenImageTagIsEmpty_ReturnsEmptyTagError()
    {
        // Arrange & Act
        Result<WorkerImageConfiguration> result = WorkerImageConfiguration.Create(
            imageTag: string.Empty,
            buildEnabled: true,
            installDotnet: false,
            installAngular: false,
            installChromium: false,
            installDocker: false);

        // Assert
        Result<WorkerImageConfiguration>.Failure failure = result.ShouldBeOfType<Result<WorkerImageConfiguration>.Failure>();
        failure.Error.Code.ShouldBe("Settings.EmptyImageTag");
    }

    [Fact]
    public void WhenImageTagIsWhitespace_ReturnsFailure()
    {
        // Arrange & Act
        Result<WorkerImageConfiguration> result = WorkerImageConfiguration.Create(
            imageTag: "   ",
            buildEnabled: true,
            installDotnet: false,
            installAngular: false,
            installChromium: false,
            installDocker: false);

        // Assert
        result.IsFailure.ShouldBeTrue();
    }

    [Theory]
    [InlineData(":latest")]
    [InlineData(":LATEST")]
    [InlineData(":Latest")]
    [InlineData("foundry-worker:latest")]
    [InlineData("ghcr.io/org/image:LATEST")]
    public void WhenImageTagEndsWithLatest_ReturnsFailure(string imageTag)
    {
        // Arrange & Act
        Result<WorkerImageConfiguration> result = WorkerImageConfiguration.Create(
            imageTag: imageTag,
            buildEnabled: false,
            installDotnet: false,
            installAngular: false,
            installChromium: false,
            installDocker: false);

        // Assert
        result.IsFailure.ShouldBeTrue();
    }

    [Theory]
    [InlineData(":latest")]
    [InlineData(":LATEST")]
    [InlineData(":Latest")]
    [InlineData("foundry-worker:latest")]
    [InlineData("ghcr.io/org/image:LATEST")]
    public void WhenImageTagEndsWithLatest_ReturnsLatestTagError(string imageTag)
    {
        // Arrange & Act
        Result<WorkerImageConfiguration> result = WorkerImageConfiguration.Create(
            imageTag: imageTag,
            buildEnabled: false,
            installDotnet: false,
            installAngular: false,
            installChromium: false,
            installDocker: false);

        // Assert
        Result<WorkerImageConfiguration>.Failure failure = result.ShouldBeOfType<Result<WorkerImageConfiguration>.Failure>();
        failure.Error.Code.ShouldBe("Settings.LatestImageTag");
    }

    [Theory]
    [InlineData("myimage:latestish")]
    [InlineData("repo:v1")]
    [InlineData("foundry-worker:local")]
    public void WhenImageTagEndsWithLatestSuffix_ButNotLatest_ReturnsSuccess(string imageTag)
    {
        // Arrange & Act
        Result<WorkerImageConfiguration> result = WorkerImageConfiguration.Create(
            imageTag: imageTag,
            buildEnabled: false,
            installDotnet: false,
            installAngular: false,
            installChromium: false,
            installDocker: false);

        // Assert
        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void WhenImageTagIs256Chars_ReturnsFailure()
    {
        // Arrange
        string imageTag = new string('a', 252) + ":v1x"; // 256 chars total

        // Act
        Result<WorkerImageConfiguration> result = WorkerImageConfiguration.Create(
            imageTag: imageTag,
            buildEnabled: false,
            installDotnet: false,
            installAngular: false,
            installChromium: false,
            installDocker: false);

        // Assert
        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void WhenImageTagIs256Chars_ReturnsImageTagTooLongError()
    {
        // Arrange
        string imageTag = new string('a', 252) + ":v1x"; // 256 chars total

        // Act
        Result<WorkerImageConfiguration> result = WorkerImageConfiguration.Create(
            imageTag: imageTag,
            buildEnabled: false,
            installDotnet: false,
            installAngular: false,
            installChromium: false,
            installDocker: false);

        // Assert
        Result<WorkerImageConfiguration>.Failure failure = result.ShouldBeOfType<Result<WorkerImageConfiguration>.Failure>();
        failure.Error.Code.ShouldBe("Settings.ImageTagTooLong");
    }

    [Fact]
    public void WhenImageTagIs255Chars_ReturnsSuccess()
    {
        // Arrange
        string imageTag = new string('a', 251) + ":v1x"; // 255 chars total

        // Act
        Result<WorkerImageConfiguration> result = WorkerImageConfiguration.Create(
            imageTag: imageTag,
            buildEnabled: false,
            installDotnet: false,
            installAngular: false,
            installChromium: false,
            installDocker: false);

        // Assert
        result.IsSuccess.ShouldBeTrue();
    }
}
