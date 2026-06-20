using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.RepositoryEligibilityTests;

public sealed class FromBranchProtection
{
    [Fact]
    public void WhenAllProtectionsEnabled_EligibleIsExpected()
    {
        // Arrange
        BranchProtection protection = new("main", RejectDirectPushes: true, RejectForcePushes: true, RejectDeletion: true);

        // Act
        List<EligibilityViolation> violations = CollectViolations(protection);

        // Assert
        violations.ShouldBeEmpty();
    }

    [Fact]
    public void WhenDirectPushesAllowed_ViolationIsReported()
    {
        // Arrange
        BranchProtection protection = new("main", RejectDirectPushes: false, RejectForcePushes: true, RejectDeletion: true);

        // Act
        List<EligibilityViolation> violations = CollectViolations(protection);

        // Assert
        violations.ShouldContain(v => v.Rule == EligibilityViolation.AllowDirectPushesRule);
    }

    [Fact]
    public void WhenForcePushesAllowed_ViolationIsReported()
    {
        // Arrange
        BranchProtection protection = new("main", RejectDirectPushes: true, RejectForcePushes: false, RejectDeletion: true);

        // Act
        List<EligibilityViolation> violations = CollectViolations(protection);

        // Assert
        violations.ShouldContain(v => v.Rule == EligibilityViolation.AllowForcePushesRule);
    }

    [Fact]
    public void WhenDeletionAllowed_ViolationIsReported()
    {
        // Arrange
        BranchProtection protection = new("main", RejectDirectPushes: true, RejectForcePushes: true, RejectDeletion: false);

        // Act
        List<EligibilityViolation> violations = CollectViolations(protection);

        // Assert
        violations.ShouldContain(v => v.Rule == EligibilityViolation.AllowDeletionRule);
    }

    [Fact]
    public void WhenMultipleViolations_AllViolationsReported()
    {
        // Arrange
        BranchProtection protection = new("main", RejectDirectPushes: false, RejectForcePushes: false, RejectDeletion: true);

        // Act
        List<EligibilityViolation> violations = CollectViolations(protection);

        // Assert
        violations.ShouldSatisfyAllConditions(
            () => violations.Count.ShouldBe(2),
            () => violations.ShouldContain(v => v.Rule == EligibilityViolation.AllowDirectPushesRule),
            () => violations.ShouldContain(v => v.Rule == EligibilityViolation.AllowForcePushesRule));
    }

    private static List<EligibilityViolation> CollectViolations(BranchProtection protection)
    {
        List<EligibilityViolation> violations = [];

        if (!protection.RejectDirectPushes)
        {
            violations.Add(EligibilityViolation.AllowDirectPushes());
        }

        if (!protection.RejectForcePushes)
        {
            violations.Add(EligibilityViolation.AllowForcePushes());
        }

        if (!protection.RejectDeletion)
        {
            violations.Add(EligibilityViolation.AllowDeletion());
        }

        return violations;
    }
}
