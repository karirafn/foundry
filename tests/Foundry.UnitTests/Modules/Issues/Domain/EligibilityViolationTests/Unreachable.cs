using Foundry.Modules.Issues.Domain;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.EligibilityViolationTests;

public sealed class Unreachable
{
    [Fact]
    public void WhenCalled_ReturnsViolationWithExpectedRule()
    {
        // Arrange
        string message = "Connection timed out.";

        // Act
        EligibilityViolation violation = EligibilityViolation.Unreachable(message);

        // Assert
        violation.Rule.ShouldBe("repository:unreachable");
    }

    [Fact]
    public void WhenCalled_ReturnsViolationWithSuppliedMessage()
    {
        // Arrange
        string message = "Connection timed out.";

        // Act
        EligibilityViolation violation = EligibilityViolation.Unreachable(message);

        // Assert
        violation.Description.ShouldBe(message);
    }
}
