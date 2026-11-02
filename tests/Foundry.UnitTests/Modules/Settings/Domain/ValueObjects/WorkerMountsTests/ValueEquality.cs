using Foundry.Modules.Settings.Domain.ValueObjects;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Domain.ValueObjects.WorkerMountsTests;

public sealed class ValueEquality
{
    [Fact]
    public void WhenMountListsHaveTheSameContent_InstancesAreEqual()
    {
        // Arrange
        WorkerMounts first = new WorkerMountsBuilder().WithMount("/skills", "/host/skills").Build().ValueOrThrow();
        WorkerMounts second = new WorkerMountsBuilder().WithMount("/skills", "/host/skills").Build().ValueOrThrow();

        // Act & Assert
        first.ShouldBe(second);
    }

    [Fact]
    public void WhenMountListsDiffer_InstancesAreNotEqual()
    {
        // Arrange
        WorkerMounts first = new WorkerMountsBuilder().WithMount("/skills", "/host/skills").Build().ValueOrThrow();
        WorkerMounts second = new WorkerMountsBuilder().WithMount("/skills", "/host/other").Build().ValueOrThrow();

        // Act & Assert
        first.ShouldNotBe(second);
    }
}
