using Foundry.Modules.Workers.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Domain.ValueObjects.FailureReasonTests;

public sealed class TimedOut
{
    [Fact]
    public void WhenTwoInstancesCreated_AreEqual()
    {
        // Arrange
        FailureReason.TimedOut a = new();
        FailureReason.TimedOut b = new();

        // Act

        // Assert
        a.ShouldBe(b);
    }

    [Fact]
    public void WhenAssignedToBaseType_IsTimedOut()
    {
        // Arrange
        FailureReason reason = new FailureReason.TimedOut();

        // Act

        // Assert
        reason.ShouldBeOfType<FailureReason.TimedOut>();
    }
}
