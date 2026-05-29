using Foundry.Modules.Workers.Features;

using Microsoft.Extensions.Options;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.WorkerOptionsValidatorTests;

public sealed class Validate
{
    private readonly WorkerOptionsValidator _sut = new();

    [Fact]
    public void WhenApiKeyIsEmpty_ReturnsFailure()
    {
        // Arrange
        WorkerOptions options = new() { ApiKey = string.Empty };

        // Act
        ValidateOptionsResult result = _sut.Validate(null, options);

        // Assert
        result.Failed.ShouldBeTrue();
    }

    [Fact]
    public void WhenApiKeyIsWhitespace_ReturnsFailure()
    {
        // Arrange
        WorkerOptions options = new() { ApiKey = "   " };

        // Act
        ValidateOptionsResult result = _sut.Validate(null, options);

        // Assert
        result.Failed.ShouldBeTrue();
    }

    [Fact]
    public void WhenApiKeyIsNonEmpty_ReturnsSuccess()
    {
        // Arrange
        WorkerOptions options = new() { ApiKey = "sk-ant-api-key" };

        // Act
        ValidateOptionsResult result = _sut.Validate(null, options);

        // Assert
        result.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void WhenApiKeyIsEmpty_FailureMessageMentionsApiKey()
    {
        // Arrange
        WorkerOptions options = new() { ApiKey = string.Empty };

        // Act
        ValidateOptionsResult result = _sut.Validate(null, options);

        // Assert
        string failureMessage = result.Failures.ShouldHaveSingleItem();
        failureMessage.ShouldContain("ApiKey");
    }

    [Fact]
    public void WhenConfigPathIsEmpty_ReturnsFailure()
    {
        // Arrange
        WorkerOptions options = new() { ApiKey = "sk-ant-key", ConfigPath = string.Empty };

        // Act
        ValidateOptionsResult result = _sut.Validate(null, options);

        // Assert
        result.Failed.ShouldBeTrue();
    }

    [Fact]
    public void WhenReportsPathIsEmpty_ReturnsFailure()
    {
        // Arrange
        WorkerOptions options = new() { ApiKey = "sk-ant-key", ReportsPath = string.Empty };

        // Act
        ValidateOptionsResult result = _sut.Validate(null, options);

        // Assert
        result.Failed.ShouldBeTrue();
    }

    [Fact]
    public void WhenConfigPathContainsTraversal_ReturnsFailure()
    {
        // Arrange
        WorkerOptions options = new() { ApiKey = "sk-ant-key", ConfigPath = "../etc/workers/config" };

        // Act
        ValidateOptionsResult result = _sut.Validate(null, options);

        // Assert
        result.Failed.ShouldBeTrue();
    }

    [Fact]
    public void WhenReportsPathContainsTraversal_ReturnsFailure()
    {
        // Arrange
        WorkerOptions options = new() { ApiKey = "sk-ant-key", ReportsPath = "../../outside/reports" };

        // Act
        ValidateOptionsResult result = _sut.Validate(null, options);

        // Assert
        result.Failed.ShouldBeTrue();
    }

    [Fact]
    public void WhenConfigPathContainsTraversal_FailureMessageMentionsConfigPath()
    {
        // Arrange
        WorkerOptions options = new() { ApiKey = "sk-ant-key", ConfigPath = "../unsafe" };

        // Act
        ValidateOptionsResult result = _sut.Validate(null, options);

        // Assert
        IEnumerable<string> failures = result.Failures.ShouldNotBeNull();
        failures.ShouldContain(f => f.Contains("ConfigPath"));
    }

    [Fact]
    public void WhenReportsPathContainsTraversal_FailureMessageMentionsReportsPath()
    {
        // Arrange
        WorkerOptions options = new() { ApiKey = "sk-ant-key", ReportsPath = "../unsafe" };

        // Act
        ValidateOptionsResult result = _sut.Validate(null, options);

        // Assert
        IEnumerable<string> failures = result.Failures.ShouldNotBeNull();
        failures.ShouldContain(f => f.Contains("ReportsPath"));
    }

    [Fact]
    public void WhenImageIsEmpty_ReturnsFailure()
    {
        // Arrange
        WorkerOptions options = new() { ApiKey = "sk-ant-key", Image = string.Empty };

        // Act
        ValidateOptionsResult result = _sut.Validate(null, options);

        // Assert
        result.Failed.ShouldBeTrue();
    }

    [Fact]
    public void WhenImageIsEmpty_FailureMessageMentionsImage()
    {
        // Arrange
        WorkerOptions options = new() { ApiKey = "sk-ant-key", Image = string.Empty };

        // Act
        ValidateOptionsResult result = _sut.Validate(null, options);

        // Assert
        IEnumerable<string> failures = result.Failures.ShouldNotBeNull();
        failures.ShouldContain(f => f.Contains("Image"));
    }

    [Fact]
    public void WhenImageEndsWithLatestTag_ReturnsFailure()
    {
        // Arrange
        WorkerOptions options = new() { ApiKey = "sk-ant-key", Image = "ghcr.io/anthropics/claude-code:latest" };

        // Act
        ValidateOptionsResult result = _sut.Validate(null, options);

        // Assert
        result.Failed.ShouldBeTrue();
    }

    [Fact]
    public void WhenImageEndsWithLatestTag_FailureMessageMentionsPinning()
    {
        // Arrange
        WorkerOptions options = new() { ApiKey = "sk-ant-key", Image = "ghcr.io/anthropics/claude-code:latest" };

        // Act
        ValidateOptionsResult result = _sut.Validate(null, options);

        // Assert
        IEnumerable<string> failures = result.Failures.ShouldNotBeNull();
        failures.ShouldContain(f => f.Contains(":latest"));
    }

    [Fact]
    public void WhenAllValid_ReturnsSuccess()
    {
        // Arrange
        WorkerOptions options = new()
        {
            ApiKey = "sk-ant-key",
            Image = "ghcr.io/anthropics/claude-code:v1.0",
            ConfigPath = "./workers/config",
            ReportsPath = "./data/reports",
        };

        // Act
        ValidateOptionsResult result = _sut.Validate(null, options);

        // Assert
        result.Succeeded.ShouldBeTrue();
    }
}
