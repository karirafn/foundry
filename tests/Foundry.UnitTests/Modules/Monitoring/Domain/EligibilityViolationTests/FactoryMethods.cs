using Foundry.Modules.Monitoring.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.EligibilityViolationTests;

public sealed class FactoryMethods
{
    [Fact]
    public void AllowDirectPushes_SetsExpectedRuleAndDescription()
    {
        // Arrange

        // Act
        EligibilityViolation violation = EligibilityViolation.AllowDirectPushes();

        // Assert
        violation.ShouldSatisfyAllConditions(
            () => violation.Rule.ShouldBe(EligibilityViolation.AllowDirectPushesRule),
            () => violation.Description.ShouldBe("Allows direct pushes to the protected branch."));
    }

    [Fact]
    public void AllowForcePushes_SetsExpectedRuleAndDescription()
    {
        // Arrange

        // Act
        EligibilityViolation violation = EligibilityViolation.AllowForcePushes();

        // Assert
        violation.ShouldSatisfyAllConditions(
            () => violation.Rule.ShouldBe(EligibilityViolation.AllowForcePushesRule),
            () => violation.Description.ShouldBe("Allows force pushes to the protected branch."));
    }

    [Fact]
    public void AllowDeletion_SetsExpectedRuleAndDescription()
    {
        // Arrange

        // Act
        EligibilityViolation violation = EligibilityViolation.AllowDeletion();

        // Assert
        violation.ShouldSatisfyAllConditions(
            () => violation.Rule.ShouldBe(EligibilityViolation.AllowDeletionRule),
            () => violation.Description.ShouldBe("Allows deletion of the protected branch."));
    }

    [Fact]
    public void Unreachable_SetsExpectedRuleAndDescription()
    {
        // Arrange

        // Act
        EligibilityViolation violation = EligibilityViolation.Unreachable();

        // Assert
        violation.ShouldSatisfyAllConditions(
            () => violation.Rule.ShouldBe(EligibilityViolation.UnreachableRule),
            () => violation.Description.ShouldBe("Branch protection could not be verified."));
    }

    [Fact]
    public void NoCredential_SetsExpectedRuleAndDescription()
    {
        // Arrange
        const string namespaceName = "myorg";

        // Act
        EligibilityViolation violation = EligibilityViolation.NoCredential(namespaceName);

        // Assert
        violation.ShouldSatisfyAllConditions(
            () => violation.Rule.ShouldBe($"no-credential:{namespaceName}"),
            () => violation.Description.ShouldBe($"no credential for namespace {namespaceName}"));
    }
}
