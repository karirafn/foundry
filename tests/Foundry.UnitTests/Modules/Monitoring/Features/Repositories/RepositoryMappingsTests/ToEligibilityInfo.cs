using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Repositories;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.Repositories.RepositoryMappingsTests;

public sealed class ToEligibilityInfo
{
    [Fact]
    public void WhenEligibilityIsNull_ReturnsNull()
    {
        // Arrange / Act
        RepositoryEligibilityInfo? result = RepositoryMappings.ToEligibilityInfo(null);

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public void WhenEligibilityIsEligible_ReturnsEligibleStatusWithNullReason()
    {
        // Arrange
        RepositoryEligibility.Eligible eligible = new();

        // Act
        RepositoryEligibilityInfo? result = RepositoryMappings.ToEligibilityInfo(eligible);

        // Assert
        RepositoryEligibilityInfo info = result.ShouldNotBeNull();
        info.ShouldSatisfyAllConditions(
            () => info.Status.ShouldBe("eligible"),
            () => info.Reason.ShouldBeNull(),
            () => info.Violations.ShouldBeEmpty());
    }

    [Fact]
    public void WhenEligibilityIsIneligible_ReturnsIneligibleStatusWithNullReason()
    {
        // Arrange
        RepositoryEligibility.Ineligible ineligible = new(
            [EligibilityViolation.AllowDirectPushes()]);

        // Act
        RepositoryEligibilityInfo? result = RepositoryMappings.ToEligibilityInfo(ineligible);

        // Assert
        RepositoryEligibilityInfo info = result.ShouldNotBeNull();
        info.ShouldSatisfyAllConditions(
            () => info.Status.ShouldBe("ineligible"),
            () => info.Reason.ShouldBeNull(),
            () => info.Violations.ShouldHaveSingleItem());
    }

    [Fact]
    public void WhenEligibilityIsUnreachableWithNeverProbedReason_ReturnsNeverProbedToken()
    {
        // Arrange
        RepositoryEligibility.Unreachable unreachable = new(UnreachableReason.NeverProbed);

        // Act
        RepositoryEligibilityInfo? result = RepositoryMappings.ToEligibilityInfo(unreachable);

        // Assert
        RepositoryEligibilityInfo info = result.ShouldNotBeNull();
        info.ShouldSatisfyAllConditions(
            () => info.Status.ShouldBe("unreachable"),
            () => info.Reason.ShouldBe(RepositoryEligibilityInfo.NeverProbedReason));
    }

    [Fact]
    public void WhenEligibilityIsUnreachableWithRateLimitedReason_ReturnsRateLimitedToken()
    {
        // Arrange
        RepositoryEligibility.Unreachable unreachable = new(UnreachableReason.RateLimited);

        // Act
        RepositoryEligibilityInfo? result = RepositoryMappings.ToEligibilityInfo(unreachable);

        // Assert
        RepositoryEligibilityInfo info = result.ShouldNotBeNull();
        info.ShouldSatisfyAllConditions(
            () => info.Status.ShouldBe("unreachable"),
            () => info.Reason.ShouldBe(RepositoryEligibilityInfo.RateLimitedReason));
    }

    [Fact]
    public void WhenEligibilityIsUnreachableWithBranchRulesUnavailableReason_ReturnsBranchRulesUnavailableToken()
    {
        // Arrange
        RepositoryEligibility.Unreachable unreachable = new(UnreachableReason.BranchRulesUnavailable);

        // Act
        RepositoryEligibilityInfo? result = RepositoryMappings.ToEligibilityInfo(unreachable);

        // Assert
        RepositoryEligibilityInfo info = result.ShouldNotBeNull();
        info.ShouldSatisfyAllConditions(
            () => info.Status.ShouldBe("unreachable"),
            () => info.Reason.ShouldBe(RepositoryEligibilityInfo.BranchRulesUnavailableReason));
    }
}
