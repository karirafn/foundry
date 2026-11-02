using Foundry.Modules.Settings.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Domain.ValueObjects.WorkerResourceLimitsTests;

public sealed class Default
{
    [Fact]
    public void ExposesDocumentedDefaults()
    {
        // Arrange & Act
        WorkerResourceLimits limits = WorkerResourceLimits.Default;

        // Assert
        limits.ShouldSatisfyAllConditions(
            () => limits.MemoryLimitMb.ShouldBe(8192),
            () => limits.CpuLimit.ShouldBe(2.0),
            () => limits.PidsLimit.ShouldBe(512));
    }
}
