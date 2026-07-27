using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Shared;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.MonitoredRepositoryTests;

public sealed class Create
{
    private static RepositorySlug ValidSlug =>
        RepositorySlug.Create("octocat/hello-world").ValueOrThrow();

    [Fact]
    public void WhenAllParametersAreValid_ReturnsMonitoredRepositoryWithCorrectProperties()
    {
        // Arrange
        RepositorySlug slug = ValidSlug;
        TimeSpan pollInterval = TimeSpan.FromMinutes(5);

        // Act
        MonitoredRepository repository = MonitoredRepository.Create(slug, "github.com", pollInterval);

        // Assert
        repository.ShouldSatisfyAllConditions(
            () => repository.Slug.ShouldBe(slug),
            () => repository.Host.ShouldBe("github.com"),
            () => repository.PollInterval.ShouldBe(pollInterval),
            () => repository.IsActive.ShouldBeTrue(),
            () => repository.LastPolledAt.ShouldBeNull());
    }

    [Fact]
    public void WhenCreatedWithNullPollInterval_HasNullPollInterval()
    {
        // Arrange
        RepositorySlug slug = ValidSlug;

        // Act
        MonitoredRepository repository = MonitoredRepository.Create(slug, "github.com", null);

        // Assert
        repository.PollInterval.ShouldBeNull();
    }

    [Fact]
    public void WhenCreated_AssignsNewId()
    {
        // Arrange
        RepositorySlug slug = ValidSlug;

        // Act
        MonitoredRepository a = MonitoredRepository.Create(slug, "github.com", null);
        MonitoredRepository b = MonitoredRepository.Create(slug, "github.com", null);

        // Assert
        a.Id.ShouldNotBe(b.Id);
    }
}
