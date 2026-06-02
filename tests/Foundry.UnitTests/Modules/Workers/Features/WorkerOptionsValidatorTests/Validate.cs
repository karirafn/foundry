using Foundry.Modules.Workers.Features;

using Microsoft.Extensions.Options;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.WorkerOptionsValidatorTests;

public sealed class Validate
{
    private readonly WorkerOptionsValidator _sut = new();

    [Fact]
    public void WhenNeitherCredentialSet_ReturnsFailure()
    {
        // Arrange
        WorkerOptions options = new() { ApiKey = string.Empty, OAuthToken = string.Empty };

        // Act
        ValidateOptionsResult result = _sut.Validate(null, options);

        // Assert
        result.Failed.ShouldBeTrue();
    }

    [Fact]
    public void WhenBothCredentialsAreWhitespaceOnly_ReturnsFailure()
    {
        // Arrange
        WorkerOptions options = new() { ApiKey = "   ", OAuthToken = "   " };

        // Act
        ValidateOptionsResult result = _sut.Validate(null, options);

        // Assert
        result.Failed.ShouldBeTrue();
    }

    [Fact]
    public void WhenOnlyApiKeySet_ReturnsSuccess()
    {
        // Arrange
        WorkerOptions options = new() { ApiKey = "sk-ant-api-key", OAuthToken = string.Empty };

        // Act
        ValidateOptionsResult result = _sut.Validate(null, options);

        // Assert
        result.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void WhenNeitherCredentialSet_FailureMessageMentionsBothOptions()
    {
        // Arrange
        WorkerOptions options = new() { ApiKey = string.Empty, OAuthToken = string.Empty };

        // Act
        ValidateOptionsResult result = _sut.Validate(null, options);

        // Assert
        IEnumerable<string> failures = result.Failures.ShouldNotBeNull();
        failures.ShouldContain(f => f.Contains("ApiKey") && f.Contains("OAuthToken"));
    }

    [Fact]
    public void WhenBothCredentialsSet_ReturnsFailure()
    {
        // Arrange
        WorkerOptions options = new() { ApiKey = "sk-ant-api-key", OAuthToken = "valid-oauth-token" };

        // Act
        ValidateOptionsResult result = _sut.Validate(null, options);

        // Assert
        result.Failed.ShouldBeTrue();
    }

    [Fact]
    public void WhenBothCredentialsSet_FailureMessageMentionsNotBoth()
    {
        // Arrange
        WorkerOptions options = new() { ApiKey = "sk-ant-api-key", OAuthToken = "valid-oauth-token" };

        // Act
        ValidateOptionsResult result = _sut.Validate(null, options);

        // Assert
        IEnumerable<string> failures = result.Failures.ShouldNotBeNull();
        failures.ShouldContain(f => f.Contains("not both"));
    }

    [Fact]
    public void WhenOnlyOAuthTokenSet_WithEmptyApiKey_ReturnsSuccess()
    {
        // Arrange
        WorkerOptions options = new() { ApiKey = string.Empty, OAuthToken = "valid-oauth-token" };

        // Act
        ValidateOptionsResult result = _sut.Validate(null, options);

        // Assert
        result.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void WhenOnlyOAuthTokenSet_WithWhitespaceApiKey_ReturnsSuccess()
    {
        // Arrange
        WorkerOptions options = new() { ApiKey = "   ", OAuthToken = "valid-oauth-token" };

        // Act
        ValidateOptionsResult result = _sut.Validate(null, options);

        // Assert
        result.Succeeded.ShouldBeTrue();
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
    public void WhenWorkerPromptTemplateIsEmpty_ReturnsFailure()
    {
        // Arrange
        WorkerOptions options = new()
        {
            ApiKey = "sk-ant-key",
            Image = "ghcr.io/anthropics/claude-code:v1.0",
            ConfigPath = "./workers/config",
            ReportsPath = "./data/reports",
            WorkerPromptTemplate = string.Empty,
        };

        // Act
        ValidateOptionsResult result = _sut.Validate(null, options);

        // Assert
        result.Failed.ShouldBeTrue();
    }

    [Fact]
    public void WhenWorkerPromptTemplateMissesIssueNumberToken_ReturnsFailure()
    {
        // Arrange
        WorkerOptions options = new()
        {
            ApiKey = "sk-ant-key",
            Image = "ghcr.io/anthropics/claude-code:v1.0",
            ConfigPath = "./workers/config",
            ReportsPath = "./data/reports",
            WorkerPromptTemplate = "Implement the issue.",
        };

        // Act
        ValidateOptionsResult result = _sut.Validate(null, options);

        // Assert
        result.Failed.ShouldBeTrue();
    }

    [Fact]
    public void WhenWorkerPromptTemplateMissesIssueNumberToken_FailureMessageMentionsToken()
    {
        // Arrange
        WorkerOptions options = new()
        {
            ApiKey = "sk-ant-key",
            Image = "ghcr.io/anthropics/claude-code:v1.0",
            ConfigPath = "./workers/config",
            ReportsPath = "./data/reports",
            WorkerPromptTemplate = "Implement the issue.",
        };

        // Act
        ValidateOptionsResult result = _sut.Validate(null, options);

        // Assert
        IEnumerable<string> failures = result.Failures.ShouldNotBeNull();
        failures.ShouldContain(f => f.Contains("{issueNumber}"));
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
