using Foundry.Modules.Issues.Domain;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.EligibilityViolationTests;

public sealed class AllowForcePushes
{
    [Fact]
    public void WhenCalled_ReturnsViolationWithExpectedRule()
    {
        // Arrange

        // Act
        EligibilityViolation violation = EligibilityViolation.AllowForcePushes();

        // Assert
        violation.Rule.ShouldBe("branch-protection:allow-force-pushes");
    }

    [Fact]
    public void WhenCalled_ReturnsViolationWithNonEmptyDescription()
    {
        // Arrange

        // Act
        EligibilityViolation violation = EligibilityViolation.AllowForcePushes();

        // Assert
        violation.Description.ShouldNotBeNullOrWhiteSpace();
    }
}
