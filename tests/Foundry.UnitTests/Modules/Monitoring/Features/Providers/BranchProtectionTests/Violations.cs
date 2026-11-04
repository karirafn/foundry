using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.Providers.BranchProtectionTests;

public sealed class Violations
{
    [Fact]
    public void WhenEveryRuleIsEnforced_IsEmpty()
    {
        // Arrange
        BranchProtection sut = new("main", RejectDirectPushes: true, RejectForcePushes: true, RejectDeletion: true);

        // Act
        IReadOnlyList<EligibilityViolation> violations = sut.Violations;

        // Assert
        violations.ShouldBeEmpty();
    }

    [Fact]
    public void WhenNoRuleIsEnforced_ListsEveryBranchProtectionViolationInOrder()
    {
        // Arrange
        BranchProtection sut = new("main", RejectDirectPushes: false, RejectForcePushes: false, RejectDeletion: false);

        // Act
        IReadOnlyList<EligibilityViolation> violations = sut.Violations;

        // Assert
        violations.ShouldBe(
        [
            EligibilityViolation.AllowDirectPushes(),
            EligibilityViolation.AllowForcePushes(),
            EligibilityViolation.AllowDeletion(),
        ]);
    }

    [Fact]
    public void WhenOnlyDeletionIsAllowed_ListsOnlyTheDeletionViolation()
    {
        // Arrange
        BranchProtection sut = new("main", RejectDirectPushes: true, RejectForcePushes: true, RejectDeletion: false);

        // Act
        IReadOnlyList<EligibilityViolation> violations = sut.Violations;

        // Assert
        violations.ShouldBe([EligibilityViolation.AllowDeletion()]);
    }
}
