using Foundry.Modules.Issues.Domain;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.EligibilityViolationTests;

public sealed class AllowDirectPushes
{
    [Fact]
    public void WhenCalled_ReturnsViolationWithExpectedRule()
    {
        // Arrange

        // Act
        EligibilityViolation violation = EligibilityViolation.AllowDirectPushes();

        // Assert
        violation.Rule.ShouldBe("branch-protection:allow-direct-pushes");
    }

    [Fact]
    public void WhenCalled_ReturnsViolationWithNonEmptyDescription()
    {
        // Arrange

        // Act
        EligibilityViolation violation = EligibilityViolation.AllowDirectPushes();

        // Assert
        violation.Description.ShouldNotBeNullOrWhiteSpace();
    }
}
