using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Eligibility;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.Eligibility.EligibilityComposerTests;

public sealed class EvaluateFromBranchProtection
{
    [Fact]
    public void WhenBranchProtectionResultFails_ReturnsUnreachableWithBranchRulesUnavailable()
    {
        // Arrange
        Result<BranchProtection> result = Result<BranchProtection>.Fail(
            new Error("Provider.Error", "Branch rules endpoint returned 503"));

        // Act
        RepositoryEligibility eligibility = EligibilityComposer.EvaluateFromBranchProtection(result);

        // Assert
        RepositoryEligibility.Unreachable unreachable = eligibility.ShouldBeOfType<RepositoryEligibility.Unreachable>();
        unreachable.Reason.ShouldBe(UnreachableReason.BranchRulesUnavailable);
    }

    [Fact]
    public void WhenBranchProtectionResultSucceeds_AndAllProtected_ReturnsEligible()
    {
        // Arrange
        BranchProtection protection = new("main", RejectDirectPushes: true, RejectForcePushes: true, RejectDeletion: true);
        Result<BranchProtection> result = Result<BranchProtection>.Ok(protection);

        // Act
        RepositoryEligibility eligibility = EligibilityComposer.EvaluateFromBranchProtection(result);

        // Assert
        eligibility.ShouldBeOfType<RepositoryEligibility.Eligible>();
    }

    [Fact]
    public void WhenBranchProtectionResultSucceeds_AndViolation_ReturnsIneligible()
    {
        // Arrange
        BranchProtection protection = new("main", RejectDirectPushes: false, RejectForcePushes: true, RejectDeletion: true);
        Result<BranchProtection> result = Result<BranchProtection>.Ok(protection);

        // Act
        RepositoryEligibility eligibility = EligibilityComposer.EvaluateFromBranchProtection(result);

        // Assert
        RepositoryEligibility.Ineligible ineligible = eligibility.ShouldBeOfType<RepositoryEligibility.Ineligible>();
        ineligible.Violations.ShouldContain(v => v.Rule == EligibilityViolation.AllowDirectPushesRule);
    }
}
