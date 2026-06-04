using Foundry.Modules.Issues.Domain;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.EligibilityViolationTests;

public sealed class AllowDeletion
{
    [Fact]
    public void WhenCalled_ReturnsViolationWithExpectedRule()
    {
        // Arrange

        // Act
        EligibilityViolation violation = EligibilityViolation.AllowDeletion();

        // Assert
        violation.Rule.ShouldBe("branch-protection:allow-deletion");
    }

    [Fact]
    public void WhenCalled_ReturnsViolationWithNonEmptyDescription()
    {
        // Arrange

        // Act
        EligibilityViolation violation = EligibilityViolation.AllowDeletion();

        // Assert
        violation.Description.ShouldNotBeNullOrWhiteSpace();
    }
}
