using Foundry.Modules.Workers.Features.ContainerSpec;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.ContainerSpec.HostPathSecurityTests;

public sealed class IsSensitiveHostPathTests
{
    [Theory]
    [InlineData("/etc")]
    [InlineData("/proc")]
    [InlineData("/sys")]
    [InlineData("/dev")]
    [InlineData("/run")]
    [InlineData("/var/run")]
    public void WhenPathIsExactSensitivePrefix_ReturnsTrue(string path)
    {
        // Arrange / Act
        bool result = HostPathSecurity.IsSensitiveHostPath(path);

        // Assert
        result.ShouldBeTrue();
    }

    [Theory]
    [InlineData("/etc/passwd")]
    [InlineData("/proc/self/environ")]
    [InlineData("/sys/class/net")]
    [InlineData("/dev/mem")]
    [InlineData("/run/secrets")]
    [InlineData("/var/run/secrets")]
    public void WhenPathIsSubpathOfSensitivePrefix_ReturnsTrue(string path)
    {
        // Arrange / Act
        bool result = HostPathSecurity.IsSensitiveHostPath(path);

        // Assert
        result.ShouldBeTrue();
    }

    [Theory]
    [InlineData("/etc/")]
    [InlineData("/proc/")]
    [InlineData("/var/run/")]
    public void WhenPathIsExactSensitivePrefixWithTrailingSlash_ReturnsTrue(string path)
    {
        // Arrange / Act
        bool result = HostPathSecurity.IsSensitiveHostPath(path);

        // Assert
        result.ShouldBeTrue();
    }

    [Theory]
    [InlineData("/home/user/.claude")]
    [InlineData("/opt/myapp")]
    [InlineData("/var/log")]
    [InlineData("/data")]
    [InlineData("/mnt/storage")]
    [InlineData(@"C:\Users\test\.claude\skills")]
    public void WhenPathIsNotSensitive_ReturnsFalse(string path)
    {
        // Arrange / Act
        bool result = HostPathSecurity.IsSensitiveHostPath(path);

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public void WhenPathIsDockerSocket_ReturnsTrue()
    {
        // Arrange / Act
        bool result = HostPathSecurity.IsSensitiveHostPath("/var/run/docker.sock");

        // Assert
        result.ShouldBeTrue();
    }
}
