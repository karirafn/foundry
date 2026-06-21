using Foundry.Modules.Monitoring.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.EligibilityViolationTests;

public sealed class Equals
{
    [Fact]
    public void WhenTwoViolationsHaveSameRuleAndDescription_AreEqual()
    {
        // Arrange
        EligibilityViolation a = EligibilityViolation.AllowDirectPushes();
        EligibilityViolation b = EligibilityViolation.AllowDirectPushes();

        // Act
        bool equal = a == b;

        // Assert
        equal.ShouldBeTrue();
    }

    [Fact]
    public void WhenTwoViolationsHaveDifferentRules_AreNotEqual()
    {
        // Arrange
        EligibilityViolation a = EligibilityViolation.AllowDirectPushes();
        EligibilityViolation b = EligibilityViolation.AllowForcePushes();

        // Act
        bool equal = a == b;

        // Assert
        equal.ShouldBeFalse();
    }
}
