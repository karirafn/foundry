using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.MonitoredRepositoryTests;

public sealed class SetEligibility
{
    private static RepositorySlug ValidSlug =>
        ((Result<RepositorySlug>.Success)RepositorySlug.Create("octocat/hello-world")).Value;

    private static MonitoredRepository CreateRepository() =>
        MonitoredRepository.Create(ValidSlug, AccountId.New(), null);

    [Fact]
    public void WhenEligibleSet_EligibilityStatusIsEligible()
    {
        // Arrange
        MonitoredRepository repository = CreateRepository();
        RepositoryEligibility eligibility = new RepositoryEligibility.Eligible();

        // Act
        repository.SetEligibility(eligibility);

        // Assert
        repository.ShouldSatisfyAllConditions(
            () => repository.Eligibility.ShouldBeOfType<RepositoryEligibility.Eligible>(),
            () => repository.EligibilityStatus.ShouldBe("eligible"));
    }

    [Fact]
    public void WhenIneligibleSet_EligibilityStatusIsIneligibleAndViolationsAccessible()
    {
        // Arrange
        MonitoredRepository repository = CreateRepository();
        IReadOnlyList<EligibilityViolation> violations =
        [
            EligibilityViolation.AllowDirectPushes(),
        ];
        RepositoryEligibility eligibility = new RepositoryEligibility.Ineligible(violations);

        // Act
        repository.SetEligibility(eligibility);

        // Assert
        RepositoryEligibility.Ineligible ineligible =
            repository.Eligibility.ShouldNotBeNull().ShouldBeOfType<RepositoryEligibility.Ineligible>();
        repository.ShouldSatisfyAllConditions(
            () => repository.EligibilityStatus.ShouldBe("ineligible"),
            () => ineligible.Violations.Count.ShouldBe(1),
            () => ineligible.Violations.ShouldContain(v => v.Rule == EligibilityViolation.AllowDirectPushesRule));
    }

    [Fact]
    public void WhenUnreachableSet_EligibilityStatusIsUnreachable()
    {
        // Arrange
        MonitoredRepository repository = CreateRepository();
        RepositoryEligibility eligibility = new RepositoryEligibility.Unreachable();

        // Act
        repository.SetEligibility(eligibility);

        // Assert
        repository.ShouldSatisfyAllConditions(
            () => repository.Eligibility.ShouldBeOfType<RepositoryEligibility.Unreachable>(),
            () => repository.EligibilityStatus.ShouldBe("unreachable"));
    }

    [Fact]
    public void WhenEligibilityChangedMultipleTimes_VoAndStatusStayInSync()
    {
        // Arrange
        MonitoredRepository repository = CreateRepository();
        repository.SetEligibility(new RepositoryEligibility.Eligible());

        // Act
        repository.SetEligibility(new RepositoryEligibility.Unreachable());

        // Assert
        repository.ShouldSatisfyAllConditions(
            () => repository.Eligibility.ShouldBeOfType<RepositoryEligibility.Unreachable>(),
            () => repository.EligibilityStatus.ShouldBe("unreachable"));
    }
}
