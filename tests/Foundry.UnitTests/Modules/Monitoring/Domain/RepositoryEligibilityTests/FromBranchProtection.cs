using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.RepositoryEligibilityTests;

public sealed class FromBranchProtection
{
    private static BranchProtection CleanProtection => new("main", true, true, true);

    [Fact]
    public void WhenResultIsSuccessAndNoViolations_ReturnsEligible()
    {
        // Arrange
        Result<BranchProtection> result = Result<BranchProtection>.Ok(CleanProtection);

        // Act
        RepositoryEligibility eligibility = RepositoryEligibility.FromBranchProtection(result);

        // Assert
        eligibility.ShouldBeOfType<RepositoryEligibility.Eligible>();
    }

    [Fact]
    public void WhenResultIsSuccessWithViolations_ReturnsIneligibleWithCorrectViolations()
    {
        // Arrange
        BranchProtection protection = new("main", RejectDirectPushes: false, RejectForcePushes: false, RejectDeletion: true);
        Result<BranchProtection> result = Result<BranchProtection>.Ok(protection);

        // Act
        RepositoryEligibility eligibility = RepositoryEligibility.FromBranchProtection(result);

        // Assert
        RepositoryEligibility.Ineligible ineligible = eligibility.ShouldBeOfType<RepositoryEligibility.Ineligible>();
        ineligible.Violations.ShouldSatisfyAllConditions(
            () => ineligible.Violations.Count.ShouldBe(2),
            () => ineligible.Violations.ShouldContain(v => v.Rule == EligibilityViolation.AllowDirectPushesRule),
            () => ineligible.Violations.ShouldContain(v => v.Rule == EligibilityViolation.AllowForcePushesRule));
    }

    [Fact]
    public void WhenResultIsFailure_ReturnsUnreachable()
    {
        // Arrange
        Result<BranchProtection> result = Result<BranchProtection>.Fail(
            new Error("Provider.Unreachable", "Branch protection could not be fetched."));

        // Act
        RepositoryEligibility eligibility = RepositoryEligibility.FromBranchProtection(result);

        // Assert
        eligibility.ShouldBeOfType<RepositoryEligibility.Unreachable>();
    }
}
