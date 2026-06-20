using Foundry.Modules.Monitoring.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.RepositoryEligibilityTests;

public sealed class Ineligible
{
    [Fact]
    public void WhenViolationsListIsEmpty_ThrowsArgumentException()
    {
        // Arrange
        IReadOnlyList<EligibilityViolation> emptyViolations = [];

        // Act
        ArgumentException ex = Should.Throw<ArgumentException>(
            () => new RepositoryEligibility.Ineligible(emptyViolations));

        // Assert
        ex.ParamName.ShouldBe("violations");
    }
}
