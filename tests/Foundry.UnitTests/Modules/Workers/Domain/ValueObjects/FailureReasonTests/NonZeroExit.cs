using Foundry.Modules.Workers.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Domain.ValueObjects.FailureReasonTests;

public sealed class NonZeroExit
{
    [Fact]
    public void WhenConstructed_ExitCodeIsPreserved()
    {
        // Arrange

        // Act
        FailureReason.NonZeroExit reason = new(ExitCode: 1);

        // Assert
        reason.ExitCode.ShouldBe(1);
    }

    [Fact]
    public void WhenTwoInstancesHaveSameExitCode_AreEqual()
    {
        // Arrange
        FailureReason.NonZeroExit a = new(ExitCode: 2);
        FailureReason.NonZeroExit b = new(ExitCode: 2);

        // Act

        // Assert
        a.ShouldBe(b);
    }

    [Fact]
    public void WhenTwoInstancesHaveDifferentExitCode_AreNotEqual()
    {
        // Arrange
        FailureReason.NonZeroExit a = new(ExitCode: 1);
        FailureReason.NonZeroExit b = new(ExitCode: 2);

        // Act

        // Assert
        a.ShouldNotBe(b);
    }
}
