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
            ReportsPath = "./data/reports",
        };

        // Act
        ValidateOptionsResult result = _sut.Validate(null, options);

        // Assert
        result.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void WhenMountsAndWritableMountsAreEmpty_ReturnsSuccess()
    {
        // Arrange
        WorkerOptions options = new()
        {
            ApiKey = "sk-ant-key",
            Image = "ghcr.io/anthropics/claude-code:v1.0",
            ReportsPath = "./data/reports",
            Mounts = new Dictionary<string, string>(),
            WritableMounts = new Dictionary<string, string>(),
        };

        // Act
        ValidateOptionsResult result = _sut.Validate(null, options);

        // Assert
        result.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void WhenMountsAndWritableMountsHaveDistinctContainerPaths_ReturnsSuccess()
    {
        // Arrange
        WorkerOptions options = new()
        {
            ApiKey = "sk-ant-key",
            Image = "ghcr.io/anthropics/claude-code:v1.0",
            ReportsPath = "./data/reports",
            Mounts = new Dictionary<string, string> { ["/config/"] = "/host/config" },
            WritableMounts = new Dictionary<string, string> { ["/data/"] = "/host/data" },
        };

        // Act
        ValidateOptionsResult result = _sut.Validate(null, options);

        // Assert
        result.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void WhenMountsAndWritableMountsShareContainerPath_ReturnsFailure()
    {
        // Arrange
        WorkerOptions options = new()
        {
            ApiKey = "sk-ant-key",
            Image = "ghcr.io/anthropics/claude-code:v1.0",
            ReportsPath = "./data/reports",
            Mounts = new Dictionary<string, string> { ["/shared/"] = "/host/a" },
            WritableMounts = new Dictionary<string, string> { ["/shared/"] = "/host/b" },
        };

        // Act
        ValidateOptionsResult result = _sut.Validate(null, options);

        // Assert
        result.Failed.ShouldBeTrue();
    }

    [Fact]
    public void WhenMountsAndWritableMountsShareContainerPath_FailureMessageMentionsOverlap()
    {
        // Arrange
        WorkerOptions options = new()
        {
            ApiKey = "sk-ant-key",
            Image = "ghcr.io/anthropics/claude-code:v1.0",
            ReportsPath = "./data/reports",
            Mounts = new Dictionary<string, string> { ["/shared/"] = "/host/a" },
            WritableMounts = new Dictionary<string, string> { ["/shared/"] = "/host/b" },
        };

        // Act
        ValidateOptionsResult result = _sut.Validate(null, options);

        // Assert
        IEnumerable<string> failures = result.Failures.ShouldNotBeNull();
        failures.ShouldContain(f => f.Contains("/shared/"));
    }

    [Fact]
    public void WhenMountsContainerPathIsRelative_ReturnsFailure()
    {
        // Arrange
        WorkerOptions options = new()
        {
            ApiKey = "sk-ant-key",
            Image = "ghcr.io/anthropics/claude-code:v1.0",
            ReportsPath = "./data/reports",
            Mounts = new Dictionary<string, string> { ["relative/path"] = "/host/config" },
            WritableMounts = new Dictionary<string, string>(),
        };

        // Act
        ValidateOptionsResult result = _sut.Validate(null, options);

        // Assert
        result.Failed.ShouldBeTrue();
    }

    [Fact]
    public void WhenWritableMountsContainerPathIsRelative_ReturnsFailure()
    {
        // Arrange
        WorkerOptions options = new()
        {
            ApiKey = "sk-ant-key",
            Image = "ghcr.io/anthropics/claude-code:v1.0",
            ReportsPath = "./data/reports",
            Mounts = new Dictionary<string, string>(),
            WritableMounts = new Dictionary<string, string> { ["no/leading/slash"] = "/host/data" },
        };

        // Act
        ValidateOptionsResult result = _sut.Validate(null, options);

        // Assert
        result.Failed.ShouldBeTrue();
    }

    [Fact]
    public void WhenMountsContainerPathContainsTraversalSegment_ReturnsFailure()
    {
        // Arrange
        WorkerOptions options = new()
        {
            ApiKey = "sk-ant-key",
            Image = "ghcr.io/anthropics/claude-code:v1.0",
            ReportsPath = "./data/reports",
            Mounts = new Dictionary<string, string> { ["/safe/../escape"] = "/host/config" },
            WritableMounts = new Dictionary<string, string>(),
        };

        // Act
        ValidateOptionsResult result = _sut.Validate(null, options);

        // Assert
        result.Failed.ShouldBeTrue();
    }

    [Fact]
    public void WhenWritableMountsContainerPathContainsTraversalSegment_ReturnsFailure()
    {
        // Arrange
        WorkerOptions options = new()
        {
            ApiKey = "sk-ant-key",
            Image = "ghcr.io/anthropics/claude-code:v1.0",
            ReportsPath = "./data/reports",
            Mounts = new Dictionary<string, string>(),
            WritableMounts = new Dictionary<string, string> { ["/safe/../escape"] = "/host/data" },
        };

        // Act
        ValidateOptionsResult result = _sut.Validate(null, options);

        // Assert
        result.Failed.ShouldBeTrue();
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/etc")]
    [InlineData("/proc")]
    [InlineData("/sys")]
    [InlineData("/dev")]
    [InlineData("/run")]
    public void WhenMountsContainerPathIsSensitivePrefix_ReturnsFailure(string sensitivePath)
    {
        // Arrange
        WorkerOptions options = new()
        {
            ApiKey = "sk-ant-key",
            Image = "ghcr.io/anthropics/claude-code:v1.0",
            ReportsPath = "./data/reports",
            Mounts = new Dictionary<string, string> { [sensitivePath] = "/host/config" },
            WritableMounts = new Dictionary<string, string>(),
        };

        // Act
        ValidateOptionsResult result = _sut.Validate(null, options);

        // Assert
        result.Failed.ShouldBeTrue();
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/etc")]
    [InlineData("/proc")]
    [InlineData("/sys")]
    [InlineData("/dev")]
    [InlineData("/run")]
    public void WhenWritableMountsContainerPathIsSensitivePrefix_ReturnsFailure(string sensitivePath)
    {
        // Arrange
        WorkerOptions options = new()
        {
            ApiKey = "sk-ant-key",
            Image = "ghcr.io/anthropics/claude-code:v1.0",
            ReportsPath = "./data/reports",
            Mounts = new Dictionary<string, string>(),
            WritableMounts = new Dictionary<string, string> { [sensitivePath] = "/host/data" },
        };

        // Act
        ValidateOptionsResult result = _sut.Validate(null, options);

        // Assert
        result.Failed.ShouldBeTrue();
    }

    [Theory]
    [InlineData("/etc/")]
    [InlineData("/proc/")]
    [InlineData("/sys/")]
    [InlineData("/dev/")]
    [InlineData("/run/")]
    public void WhenMountsContainerPathIsSensitivePrefixWithTrailingSlash_ReturnsFailure(string sensitivePath)
    {
        // Arrange
        WorkerOptions options = new()
        {
            ApiKey = "sk-ant-key",
            Image = "ghcr.io/anthropics/claude-code:v1.0",
            ReportsPath = "./data/reports",
            Mounts = new Dictionary<string, string> { [sensitivePath] = "/host/config" },
            WritableMounts = new Dictionary<string, string>(),
        };

        // Act
        ValidateOptionsResult result = _sut.Validate(null, options);

        // Assert
        result.Failed.ShouldBeTrue();
    }

    [Theory]
    [InlineData("/proc/self/environ")]
    [InlineData("/proc/self")]
    [InlineData("/etc/passwd")]
    [InlineData("/sys/class/net")]
    [InlineData("/etc/shadow")]
    [InlineData("/dev/mem")]
    public void WhenMountsContainerPathIsSubpathOfSensitivePrefix_ReturnsFailure(string subPath)
    {
        // Arrange
        WorkerOptions options = new()
        {
            ApiKey = "sk-ant-key",
            Image = "ghcr.io/anthropics/claude-code:v1.0",
            ReportsPath = "./data/reports",
            Mounts = new Dictionary<string, string> { [subPath] = "/host/config" },
            WritableMounts = new Dictionary<string, string>(),
        };

        // Act
        ValidateOptionsResult result = _sut.Validate(null, options);

        // Assert
        result.Failed.ShouldBeTrue();
    }

    [Theory]
    [InlineData("/proc/self/environ")]
    [InlineData("/proc/self")]
    [InlineData("/etc/passwd")]
    [InlineData("/sys/class/net")]
    public void WhenWritableMountsContainerPathIsSubpathOfSensitivePrefix_ReturnsFailure(string subPath)
    {
        // Arrange
        WorkerOptions options = new()
        {
            ApiKey = "sk-ant-key",
            Image = "ghcr.io/anthropics/claude-code:v1.0",
            ReportsPath = "./data/reports",
            Mounts = new Dictionary<string, string>(),
            WritableMounts = new Dictionary<string, string> { [subPath] = "/host/data" },
        };

        // Act
        ValidateOptionsResult result = _sut.Validate(null, options);

        // Assert
        result.Failed.ShouldBeTrue();
    }

    [Theory]
    [InlineData("/etc")]
    [InlineData("/proc")]
    [InlineData("/sys")]
    [InlineData("/dev")]
    [InlineData("/run")]
    [InlineData("/var/run")]
    [InlineData("/etc/ssh")]
    [InlineData("/proc/self")]
    [InlineData("/sys/class/net")]
    public void WhenMountsHostPathIsSensitivePrefix_ReturnsFailure(string sensitivePath)
    {
        // Arrange
        WorkerOptions options = new()
        {
            ApiKey = "sk-ant-key",
            Image = "ghcr.io/anthropics/claude-code:v1.0",
            ReportsPath = "./data/reports",
            Mounts = new Dictionary<string, string> { ["/config"] = sensitivePath },
            WritableMounts = new Dictionary<string, string>(),
        };

        // Act
        ValidateOptionsResult result = _sut.Validate(null, options);

        // Assert
        result.Failed.ShouldBeTrue();
    }

    [Theory]
    [InlineData("/etc")]
    [InlineData("/proc")]
    [InlineData("/sys")]
    [InlineData("/dev")]
    [InlineData("/run")]
    [InlineData("/var/run")]
    public void WhenWritableMountsHostPathIsSensitivePrefix_ReturnsFailure(string sensitivePath)
    {
        // Arrange
        WorkerOptions options = new()
        {
            ApiKey = "sk-ant-key",
            Image = "ghcr.io/anthropics/claude-code:v1.0",
            ReportsPath = "./data/reports",
            Mounts = new Dictionary<string, string>(),
            WritableMounts = new Dictionary<string, string> { ["/workspace"] = sensitivePath },
        };

        // Act
        ValidateOptionsResult result = _sut.Validate(null, options);

        // Assert
        result.Failed.ShouldBeTrue();
    }

    [Fact]
    public void WhenMountsHostPathIsDockerSocket_ReturnsFailure()
    {
        // Arrange
        WorkerOptions options = new()
        {
            ApiKey = "sk-ant-key",
            Image = "ghcr.io/anthropics/claude-code:v1.0",
            ReportsPath = "./data/reports",
            Mounts = new Dictionary<string, string> { ["/var/run/docker.sock"] = "/var/run/docker.sock" },
            WritableMounts = new Dictionary<string, string>(),
        };

        // Act
        ValidateOptionsResult result = _sut.Validate(null, options);

        // Assert
        result.Failed.ShouldBeTrue();
    }

    [Fact]
    public void WhenWritableMountsHostPathIsDockerSocket_ReturnsFailure()
    {
        // Arrange
        WorkerOptions options = new()
        {
            ApiKey = "sk-ant-key",
            Image = "ghcr.io/anthropics/claude-code:v1.0",
            ReportsPath = "./data/reports",
            Mounts = new Dictionary<string, string>(),
            WritableMounts = new Dictionary<string, string> { ["/var/run/docker.sock"] = "/var/run/docker.sock" },
        };

        // Act
        ValidateOptionsResult result = _sut.Validate(null, options);

        // Assert
        result.Failed.ShouldBeTrue();
    }

    [Fact]
    public void WhenMountsContainerPathIsNotSensitive_ReturnsSuccess()
    {
        // Arrange
        WorkerOptions options = new()
        {
            ApiKey = "sk-ant-key",
            Image = "ghcr.io/anthropics/claude-code:v1.0",
            ReportsPath = "./data/reports",
            Mounts = new Dictionary<string, string> { ["/config"] = "/host/config" },
            WritableMounts = new Dictionary<string, string>(),
        };

        // Act
        ValidateOptionsResult result = _sut.Validate(null, options);

        // Assert
        result.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void WhenMountsHostPathIsWindowsAbsolutePath_PassesAbsoluteCheck()
    {
        // Arrange
        WorkerOptions options = new()
        {
            ApiKey = "sk-ant-key",
            Image = "ghcr.io/anthropics/claude-code:v1.0",
            ReportsPath = "./data/reports",
            Mounts = new Dictionary<string, string> { ["/config"] = @"C:\Users\test\.claude\skills" },
            WritableMounts = new Dictionary<string, string>(),
        };

        // Act
        ValidateOptionsResult result = _sut.Validate(null, options);

        // Assert
        // Should not fail for the "must be absolute" reason — the only possible failure
        // is if the path were considered relative. Windows-style paths are accepted.
        IEnumerable<string> failures = result.Failures ?? [];
        failures.ShouldNotContain(f => f.Contains("must be absolute"));
    }

    [Fact]
    public void WhenMountsHostPathContainsTraversalSegment_ReturnsFailure()
    {
        // Arrange
        WorkerOptions options = new()
        {
            ApiKey = "sk-ant-key",
            Image = "ghcr.io/anthropics/claude-code:v1.0",
            ReportsPath = "./data/reports",
            Mounts = new Dictionary<string, string> { ["/config"] = "/host/../../escape" },
            WritableMounts = new Dictionary<string, string>(),
        };

        // Act
        ValidateOptionsResult result = _sut.Validate(null, options);

        // Assert
        result.Failed.ShouldBeTrue();
    }

    [Fact]
    public void WhenWritableMountsHostPathContainsTraversalSegment_ReturnsFailure()
    {
        // Arrange
        WorkerOptions options = new()
        {
            ApiKey = "sk-ant-key",
            Image = "ghcr.io/anthropics/claude-code:v1.0",
            ReportsPath = "./data/reports",
            Mounts = new Dictionary<string, string>(),
            WritableMounts = new Dictionary<string, string> { ["/workspace"] = "/host/../../escape" },
        };

        // Act
        ValidateOptionsResult result = _sut.Validate(null, options);

        // Assert
        result.Failed.ShouldBeTrue();
    }

    [Fact]
    public void WhenMountsHostPathIsRelative_ReturnsFailure()
    {
        // Arrange
        WorkerOptions options = new()
        {
            ApiKey = "sk-ant-key",
            Image = "ghcr.io/anthropics/claude-code:v1.0",
            ReportsPath = "./data/reports",
            Mounts = new Dictionary<string, string> { ["/config"] = "relative/path" },
            WritableMounts = new Dictionary<string, string>(),
        };

        // Act
        ValidateOptionsResult result = _sut.Validate(null, options);

        // Assert
        result.Failed.ShouldBeTrue();
    }

    [Fact]
    public void WhenWritableMountsHostPathIsRelative_ReturnsFailure()
    {
        // Arrange
        WorkerOptions options = new()
        {
            ApiKey = "sk-ant-key",
            Image = "ghcr.io/anthropics/claude-code:v1.0",
            ReportsPath = "./data/reports",
            Mounts = new Dictionary<string, string>(),
            WritableMounts = new Dictionary<string, string> { ["/workspace"] = "relative/path" },
        };

        // Act
        ValidateOptionsResult result = _sut.Validate(null, options);

        // Assert
        result.Failed.ShouldBeTrue();
    }

    [Fact]
    public void WhenMountsContainerPathWithTrailingSlashMatchesWritableMounts_ReturnsFailure()
    {
        // Arrange
        WorkerOptions options = new()
        {
            ApiKey = "sk-ant-key",
            Image = "ghcr.io/anthropics/claude-code:v1.0",
            ReportsPath = "./data/reports",
            Mounts = new Dictionary<string, string> { ["/config/"] = "/host/a" },
            WritableMounts = new Dictionary<string, string> { ["/config"] = "/host/b" },
        };

        // Act
        ValidateOptionsResult result = _sut.Validate(null, options);

        // Assert
        result.Failed.ShouldBeTrue();
    }
}
