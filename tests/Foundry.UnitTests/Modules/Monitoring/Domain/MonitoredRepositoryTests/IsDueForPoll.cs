using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Shared;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.MonitoredRepositoryTests;

public sealed class IsDueForPoll
{
    private static RepositorySlug ValidSlug =>
        RepositorySlug.Create("octocat/hello-world").ValueOrThrow();

    private static MonitoredRepository CreateRepository(TimeSpan? pollInterval = null) =>
        MonitoredRepository.Create(ValidSlug, "github.com", pollInterval);

    [Fact]
    public void WhenLastPolledAtIsNull_ReturnsTrue()
    {
        // Arrange
        MonitoredRepository repository = CreateRepository();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        TimeSpan defaultInterval = TimeSpan.FromMinutes(5);

        // Act
        bool result = repository.IsDueForPoll(defaultInterval, now);

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public void WhenElapsedTimeExceedsDefaultInterval_ReturnsTrue()
    {
        // Arrange
        MonitoredRepository repository = CreateRepository();
        DateTimeOffset polledAt = DateTimeOffset.UtcNow.AddMinutes(-10);
        repository.MarkPolled(polledAt);
        TimeSpan defaultInterval = TimeSpan.FromMinutes(5);
        DateTimeOffset now = DateTimeOffset.UtcNow;

        // Act
        bool result = repository.IsDueForPoll(defaultInterval, now);

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public void WhenElapsedTimeIsWithinDefaultInterval_ReturnsFalse()
    {
        // Arrange
        MonitoredRepository repository = CreateRepository();
        DateTimeOffset polledAt = DateTimeOffset.UtcNow.AddMinutes(-2);
        repository.MarkPolled(polledAt);
        TimeSpan defaultInterval = TimeSpan.FromMinutes(5);
        DateTimeOffset now = DateTimeOffset.UtcNow;

        // Act
        bool result = repository.IsDueForPoll(defaultInterval, now);

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public void WhenPollIntervalIsSet_UsesRepositoryPollIntervalInsteadOfDefault()
    {
        // Arrange
        TimeSpan customInterval = TimeSpan.FromMinutes(15);
        MonitoredRepository repository = CreateRepository(pollInterval: customInterval);
        DateTimeOffset polledAt = DateTimeOffset.UtcNow.AddMinutes(-10);
        repository.MarkPolled(polledAt);
        TimeSpan defaultInterval = TimeSpan.FromMinutes(5);
        DateTimeOffset now = DateTimeOffset.UtcNow;

        // Act
        bool result = repository.IsDueForPoll(defaultInterval, now);

        // Assert
        result.ShouldBeFalse();
    }
}
