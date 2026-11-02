using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Modules.Settings.Domain.ValueObjects;
using Foundry.Shared;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Domain.ValueObjects.WorkerMountsTests;

public sealed class Create
{
    [Fact]
    public void WhenMountsAreValid_ExposesThemInOrder()
    {
        // Arrange
        WorkerMountsBuilder builder = new WorkerMountsBuilder()
            .WithMount("/skills", "/home/user/skills", readOnly: true)
            .WithMount("/cache", "/home/user/cache", readOnly: false);

        // Act
        Result<WorkerMounts> result = builder.Build();

        // Assert
        WorkerMounts mounts = result.ShouldBeOfType<Result<WorkerMounts>.Success>().Value;
        mounts.Mounts.ShouldBe(
            [
                new WorkerMount("/skills", "/home/user/skills", true),
                new WorkerMount("/cache", "/home/user/cache", false),
            ]);
    }

    [Fact]
    public void WhenWindowsHostPathIsUsed_ReturnsSuccess()
    {
        // Arrange
        WorkerMountsBuilder builder = new WorkerMountsBuilder().WithMount("/skills", @"C:\dev\ai\skills");

        // Act
        Result<WorkerMounts> result = builder.Build();

        // Assert
        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void WhenWindowsHostPathUsesForwardSlashes_ReturnsSuccess()
    {
        // Arrange
        WorkerMountsBuilder builder = new WorkerMountsBuilder().WithMount("/skills", "C:/dev/ai/skills");

        // Act
        Result<WorkerMounts> result = builder.Build();

        // Assert
        result.IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData("skills", "/host/skills", SettingsErrors.MountContainerPathNotAbsoluteCode)]
    [InlineData(@"C:\skills", "/host/skills", SettingsErrors.MountContainerPathNotAbsoluteCode)]
    [InlineData("/skills", "host/skills", SettingsErrors.MountHostPathNotAbsoluteCode)]
    [InlineData("/skills", @"C:skills", SettingsErrors.MountHostPathNotAbsoluteCode)]
    [InlineData("/skills/../etc", "/host/skills", SettingsErrors.MountPathTraversalCode)]
    [InlineData("/skills", "/host/../etc", SettingsErrors.MountPathTraversalCode)]
    [InlineData("/skills", @"C:\dev\..\secrets", SettingsErrors.MountPathTraversalCode)]
    [InlineData("/", "/host/skills", SettingsErrors.SensitiveMountContainerPathCode)]
    [InlineData("/etc", "/host/skills", SettingsErrors.SensitiveMountContainerPathCode)]
    [InlineData("/proc/self", "/host/skills", SettingsErrors.SensitiveMountContainerPathCode)]
    [InlineData("/sys/", "/host/skills", SettingsErrors.SensitiveMountContainerPathCode)]
    [InlineData("/dev", "/host/skills", SettingsErrors.SensitiveMountContainerPathCode)]
    [InlineData("/run", "/host/skills", SettingsErrors.SensitiveMountContainerPathCode)]
    [InlineData("/var/run/x", "/host/skills", SettingsErrors.SensitiveMountContainerPathCode)]
    [InlineData("/skills", "/etc/ssl", SettingsErrors.SensitiveMountHostPathCode)]
    [InlineData("/skills", "/var/run/docker.sock", SettingsErrors.SensitiveMountHostPathCode)]
    [InlineData("/skills", "/proc", SettingsErrors.SensitiveMountHostPathCode)]
    public void WhenAMountPathIsRejected_FailsWithMatchingCode(
        string containerPath,
        string hostPath,
        string expectedCode)
    {
        // Arrange
        WorkerMountsBuilder builder = new WorkerMountsBuilder().WithMount(containerPath, hostPath);

        // Act
        Result<WorkerMounts> result = builder.Build();

        // Assert
        result.ShouldBeOfType<Result<WorkerMounts>.Failure>().Error.Code.ShouldBe(expectedCode);
    }

    [Theory]
    [InlineData("/skills", "/skills")]
    [InlineData("/skills", "/skills/")]
    public void WhenContainerPathIsDuplicated_FailsNamingThePath(string first, string second)
    {
        // Arrange
        WorkerMountsBuilder builder = new WorkerMountsBuilder()
            .WithMount(first, "/host/a", readOnly: true)
            .WithMount(second, "/host/b", readOnly: false);

        // Act
        Result<WorkerMounts> result = builder.Build();

        // Assert
        Error error = result.ShouldBeOfType<Result<WorkerMounts>.Failure>().Error;
        error.ShouldSatisfyAllConditions(
            () => error.Code.ShouldBe(SettingsErrors.DuplicateMountContainerPathCode),
            () => error.Message.ShouldContain(second));
    }
}
