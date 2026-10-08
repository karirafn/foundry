using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
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
        MonitoredRepository.Create(ValidSlug, "github.com", pollInterval).ValueOrThrow();

    [Fact]
    public void WhenPollIntervalAndActiveStatusProvided_UpdatesBothProperties()
    {
        // Arrange
        MonitoredRepository repository = CreateRepository(pollInterval: TimeSpan.FromMinutes(5));
        TimeSpan newPollInterval = TimeSpan.FromMinutes(10);

        // Act
        Result result = repository.Update(newPollInterval, isActive: false, maxConcurrentWorkers: 1);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        repository.ShouldSatisfyAllConditions(
            () => repository.PollInterval.ShouldBe(newPollInterval),
            () => repository.IsActive.ShouldBeFalse());
    }

    [Fact]
    public void WhenUpdated_PreservesSlug()
    {
        // Arrange
        RepositorySlug slug = ValidSlug;
        MonitoredRepository repository = MonitoredRepository.Create(slug, "github.com", null).ValueOrThrow();

        // Act
        Result result = repository.Update(TimeSpan.FromMinutes(15), isActive: true, maxConcurrentWorkers: 1);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        repository.Slug.ShouldBe(slug);
    }

    [Fact]
    public void WhenNullPollIntervalIsPassed_ClearsPollInterval()
    {
        // Arrange
        MonitoredRepository repository = CreateRepository(pollInterval: TimeSpan.FromMinutes(5));

        // Act
        Result result = repository.Update(null, isActive: true, maxConcurrentWorkers: 1);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        repository.PollInterval.ShouldBeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    [InlineData(-1)]
    public void WhenLimitIsOutOfRange_ReturnsFailureAndLeavesStateUnchanged(int invalidLimit)
    {
        // Arrange
        MonitoredRepository repository = CreateRepository(pollInterval: TimeSpan.FromMinutes(5));
        TimeSpan originalPollInterval = repository.PollInterval!.Value;
        bool originalIsActive = repository.IsActive;
        int originalLimit = repository.MaxConcurrentWorkers;

        // Act
        Result result = repository.Update(TimeSpan.FromMinutes(10), isActive: false, maxConcurrentWorkers: invalidLimit);

        // Assert
        result.IsFailure.ShouldBeTrue();
        repository.ShouldSatisfyAllConditions(
            () => repository.PollInterval.ShouldBe(originalPollInterval),
            () => repository.IsActive.ShouldBe(originalIsActive),
            () => repository.MaxConcurrentWorkers.ShouldBe(originalLimit));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(20)]
    public void WhenLimitIsValid_PersistsAllThreeFields(int validLimit)
    {
        // Arrange
        MonitoredRepository repository = CreateRepository();
        TimeSpan newPollInterval = TimeSpan.FromMinutes(7);

        // Act
        Result result = repository.Update(newPollInterval, isActive: false, maxConcurrentWorkers: validLimit);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        repository.ShouldSatisfyAllConditions(
            () => repository.PollInterval.ShouldBe(newPollInterval),
            () => repository.IsActive.ShouldBeFalse(),
            () => repository.MaxConcurrentWorkers.ShouldBe(validLimit));
    }

    [Fact]
    public void WhenPollIntervalExceedsMaximum_ReturnsFailureAndLeavesStateUnchanged()
    {
        // Arrange
        MonitoredRepository repository = CreateRepository(pollInterval: TimeSpan.FromMinutes(5));
        TimeSpan originalPollInterval = repository.PollInterval!.Value;
        bool originalIsActive = repository.IsActive;
        int originalLimit = repository.MaxConcurrentWorkers;
        TimeSpan tooLarge = TimeSpan.FromSeconds(MonitoredRepository.MaxPollIntervalSeconds + 1);

        // Act
        Result result = repository.Update(tooLarge, isActive: false, maxConcurrentWorkers: 1);

        // Assert
        result.IsFailure.ShouldBeTrue();
        repository.ShouldSatisfyAllConditions(
            () => repository.PollInterval.ShouldBe(originalPollInterval),
            () => repository.IsActive.ShouldBe(originalIsActive),
            () => repository.MaxConcurrentWorkers.ShouldBe(originalLimit));
    }

    [Fact]
    public void WhenPollIntervalIsNotPositive_ReturnsFailureAndLeavesStateUnchanged()
    {
        // Arrange
        MonitoredRepository repository = CreateRepository(pollInterval: TimeSpan.FromMinutes(5));
        TimeSpan originalPollInterval = repository.PollInterval!.Value;
        bool originalIsActive = repository.IsActive;
        int originalLimit = repository.MaxConcurrentWorkers;

        // Act
        Result result = repository.Update(TimeSpan.FromSeconds(0), isActive: false, maxConcurrentWorkers: 1);

        // Assert
        result.IsFailure.ShouldBeTrue();
        repository.ShouldSatisfyAllConditions(
            () => repository.PollInterval.ShouldBe(originalPollInterval),
            () => repository.IsActive.ShouldBe(originalIsActive),
            () => repository.MaxConcurrentWorkers.ShouldBe(originalLimit));
    }
}
