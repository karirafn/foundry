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
        MonitoredRepository repository = MonitoredRepository.Create(slug, "github.com", pollInterval).ValueOrThrow();

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
        MonitoredRepository repository = MonitoredRepository.Create(slug, "github.com", null).ValueOrThrow();

        // Assert
        repository.PollInterval.ShouldBeNull();
    }

    [Fact]
    public void WhenCreated_AssignsNewId()
    {
        // Arrange
        RepositorySlug slug = ValidSlug;

        // Act
        MonitoredRepository a = MonitoredRepository.Create(slug, "github.com", null).ValueOrThrow();
        MonitoredRepository b = MonitoredRepository.Create(slug, "github.com", null).ValueOrThrow();

        // Assert
        a.Id.ShouldNotBe(b.Id);
    }

    [Fact]
    public void WhenCreatedWithoutSpecifyingLimit_MaxConcurrentWorkersIsOne()
    {
        // Arrange
        RepositorySlug slug = ValidSlug;

        // Act
        MonitoredRepository repository = MonitoredRepository.Create(slug, "github.com", null).ValueOrThrow();

        // Assert
        repository.MaxConcurrentWorkers.ShouldBe(1);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    [InlineData(-1)]
    public void WhenCreatedWithInvalidLimit_ReturnsFailure(int invalidLimit)
    {
        // Arrange
        RepositorySlug slug = ValidSlug;

        // Act
        Result<MonitoredRepository> result = MonitoredRepository.Create(slug, "github.com", null, maxConcurrentWorkers: invalidLimit);

        // Assert
        result.IsFailure.ShouldBeTrue();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(20)]
    public void WhenCreatedWithValidLimit_ReturnsSuccessWithCorrectLimit(int validLimit)
    {
        // Arrange
        RepositorySlug slug = ValidSlug;

        // Act
        Result<MonitoredRepository> result = MonitoredRepository.Create(slug, "github.com", null, maxConcurrentWorkers: validLimit);

        // Assert
        MonitoredRepository repository = result.ValueOrThrow();
        repository.MaxConcurrentWorkers.ShouldBe(validLimit);
    }
}
