using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Shared;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.MonitoredRepositoryTests;

public sealed class Update
{
    private static RepositorySlug ValidSlug =>
        RepositorySlug.Create("octocat/hello-world").ValueOrThrow();

    private static MonitoredRepository CreateRepository(TimeSpan? pollInterval = null) =>
        MonitoredRepository.Create(ValidSlug, CredentialId.New(), "github.com", pollInterval);

    [Fact]
    public void WhenPollIntervalAndActiveStatusProvided_UpdatesBothProperties()
    {
        // Arrange
        MonitoredRepository repository = CreateRepository(pollInterval: TimeSpan.FromMinutes(5));
        TimeSpan newPollInterval = TimeSpan.FromMinutes(10);

        // Act
        repository.Update(newPollInterval, isActive: false);

        // Assert
        repository.ShouldSatisfyAllConditions(
            () => repository.PollInterval.ShouldBe(newPollInterval),
            () => repository.IsActive.ShouldBeFalse());
    }

    [Fact]
    public void WhenUpdated_PreservesSlugAndCredentialId()
    {
        // Arrange
        RepositorySlug slug = ValidSlug;
        CredentialId credentialId = CredentialId.New();
        MonitoredRepository repository = MonitoredRepository.Create(slug, credentialId, "github.com", null);

        // Act
        repository.Update(TimeSpan.FromMinutes(15), isActive: true);

        // Assert
        repository.ShouldSatisfyAllConditions(
            () => repository.Slug.ShouldBe(slug),
            () => repository.CredentialId.ShouldBe(credentialId));
    }

    [Fact]
    public void WhenNullPollIntervalIsPassed_ClearsPollInterval()
    {
        // Arrange
        MonitoredRepository repository = CreateRepository(pollInterval: TimeSpan.FromMinutes(5));

        // Act
        repository.Update(null, isActive: true);

        // Assert
        repository.PollInterval.ShouldBeNull();
    }
}
