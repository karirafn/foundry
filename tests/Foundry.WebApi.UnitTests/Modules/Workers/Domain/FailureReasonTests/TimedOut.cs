using Foundry.Modules.Workers.Domain;

using Shouldly;

using Xunit;

namespace Foundry.WebApi.UnitTests.Modules.Workers.Domain.FailureReasonTests;

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
