using Foundry.Modules.Settings.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Domain.ValueObjects.WorkerMountsTests;

public sealed class Default
{
    [Fact]
    public void IsEmpty()
    {
        // Arrange & Act
        WorkerMounts mounts = WorkerMounts.Default;

        // Assert
        mounts.Mounts.ShouldBeEmpty();
    }
}
