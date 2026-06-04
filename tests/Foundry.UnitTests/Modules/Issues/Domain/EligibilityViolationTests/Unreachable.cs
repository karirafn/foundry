using Foundry.Modules.Issues.Domain;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.EligibilityViolationTests;

public sealed class Unreachable
{
    [Fact]
    public void WhenCalled_ReturnsViolationWithExpectedRule()
    {
        // Arrange / Act
        EligibilityViolation violation = EligibilityViolation.Unreachable();

        // Assert
        violation.Rule.ShouldBe("branch-protection:unreachable");
    }

    [Fact]
    public void WhenCalled_ReturnsViolationWithFixedDescription()
    {
        // Arrange / Act
        EligibilityViolation violation = EligibilityViolation.Unreachable();

        // Assert
        violation.Description.ShouldBe(
            "Branch protection could not be verified. The provider API was unreachable or returned an error.");
    }
}
