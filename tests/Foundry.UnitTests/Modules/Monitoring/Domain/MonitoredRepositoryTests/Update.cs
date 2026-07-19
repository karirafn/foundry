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
        MonitoredRepository.Create(ValidSlug, "github.com", pollInterval);

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
    public void WhenUpdated_PreservesSlug()
    {
        // Arrange
        RepositorySlug slug = ValidSlug;
        MonitoredRepository repository = MonitoredRepository.Create(slug, "github.com", null);

        // Act
        repository.Update(TimeSpan.FromMinutes(15), isActive: true);

        // Assert
        repository.Slug.ShouldBe(slug);
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
