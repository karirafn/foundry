using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Eligibility;
using Foundry.Shared;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.Eligibility.RepositoryEligibilityEvaluatorTests;

public sealed class StoreNoCredential
{
    private static MonitoredRepository Repository(string slug) =>
        MonitoredRepository.Create(RepositorySlug.Create(slug).ValueOrThrow(), "github.com", null).ValueOrThrow();

    [Fact]
    public void WhenStoredEligible_StoresNoCredentialForTopLevelNamespaceAndReportsChange()
    {
        // Arrange
        MonitoredRepository repository = Repository("acme/platform/api");
        repository.SetEligibility(new RepositoryEligibility.Eligible());

        // Act
        bool changed = RepositoryEligibilityEvaluator.StoreNoCredential(repository);

        // Assert
        changed.ShouldBeTrue();
        RepositoryEligibility.Ineligible ineligible = repository.Eligibility.ShouldBeOfType<RepositoryEligibility.Ineligible>();
        ineligible.Violations.Select(v => v.Rule).ShouldBe(["no-credential:acme"]);
    }

    [Fact]
    public void WhenNoCredentialAlreadyStored_ReportsNoChange()
    {
        // Arrange
        MonitoredRepository repository = Repository("acme/api");
        RepositoryEligibilityEvaluator.StoreNoCredential(repository);

        // Act
        bool changed = RepositoryEligibilityEvaluator.StoreNoCredential(repository);

        // Assert
        changed.ShouldBeFalse();
    }

    [Fact]
    public void WhenStoredIneligibleForAnotherRule_ReplacesItAndReportsChange()
    {
        // Arrange
        MonitoredRepository repository = Repository("acme/api");
        repository.SetEligibility(new RepositoryEligibility.Ineligible([EligibilityViolation.CredentialInvalid()]));

        // Act
        bool changed = RepositoryEligibilityEvaluator.StoreNoCredential(repository);

        // Assert
        changed.ShouldBeTrue();
        RepositoryEligibility.Ineligible ineligible = repository.Eligibility.ShouldBeOfType<RepositoryEligibility.Ineligible>();
        ineligible.Violations.Select(v => v.Rule).ShouldBe(["no-credential:acme"]);
    }
}
