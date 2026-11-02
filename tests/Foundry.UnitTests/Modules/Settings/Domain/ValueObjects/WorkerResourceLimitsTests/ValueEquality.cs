using Foundry.Modules.Settings.Domain.ValueObjects;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Domain.ValueObjects.WorkerResourceLimitsTests;

public sealed class ValueEquality
{
    [Fact]
    public void WhenAllFieldsMatch_InstancesAreEqual()
    {
        // Arrange
        WorkerResourceLimits first = new WorkerResourceLimitsBuilder().Build().ValueOrThrow();
        WorkerResourceLimits second = new WorkerResourceLimitsBuilder().Build().ValueOrThrow();

        // Act & Assert
        first.ShouldBe(second);
    }

    [Fact]
    public void WhenAFieldDiffers_InstancesAreNotEqual()
    {
        // Arrange
        WorkerResourceLimits first = new WorkerResourceLimitsBuilder().Build().ValueOrThrow();
        WorkerResourceLimits second = new WorkerResourceLimitsBuilder().WithPidsLimit(1024).Build().ValueOrThrow();

        // Act & Assert
        first.ShouldNotBe(second);
    }
}
