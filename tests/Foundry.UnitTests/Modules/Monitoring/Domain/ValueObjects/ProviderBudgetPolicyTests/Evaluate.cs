using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.ValueObjects.ProviderBudgetPolicyTests;

public sealed class Evaluate
{
    private static readonly DateTimeOffset Now = new(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void WhenReadingIsNull_ReturnsUnknown()
    {
        // Arrange

        // Act
        ProviderBudgetHealth result = ProviderBudgetPolicy.Evaluate(reading: null, floor: 500, now: Now);

        // Assert
        result.ShouldBe(ProviderBudgetHealth.Unknown);
    }

    [Fact]
    public void WhenReadingIsFreshAndRemainingMeetsFloor_ReturnsHealthy()
    {
        // Arrange
        RateBudgetReading reading = new RateBudgetReadingBuilder()
            .WithRemaining(500)
            .WithObservedAt(Now)
            .Build();

        // Act
        ProviderBudgetHealth result = ProviderBudgetPolicy.Evaluate(reading, floor: 500, now: Now);

        // Assert
        result.ShouldBe(ProviderBudgetHealth.Healthy);
    }

    [Fact]
    public void WhenReadingIsFreshAndRemainingExceedsFloor_ReturnsHealthy()
    {
        // Arrange
        RateBudgetReading reading = new RateBudgetReadingBuilder()
            .WithRemaining(1000)
            .WithObservedAt(Now)
            .Build();

        // Act
        ProviderBudgetHealth result = ProviderBudgetPolicy.Evaluate(reading, floor: 500, now: Now);

        // Assert
        result.ShouldBe(ProviderBudgetHealth.Healthy);
    }

    [Fact]
    public void WhenReadingIsFreshAndRemainingBelowFloor_ReturnsLow()
    {
        // Arrange
        RateBudgetReading reading = new RateBudgetReadingBuilder()
            .WithRemaining(499)
            .WithObservedAt(Now)
            .Build();

        // Act
        ProviderBudgetHealth result = ProviderBudgetPolicy.Evaluate(reading, floor: 500, now: Now);

        // Assert
        result.ShouldBe(ProviderBudgetHealth.Low);
    }

    [Fact]
    public void WhenReadingIsStale_ReturnsUnknown()
    {
        // Arrange — reading observed just past the staleness window
        DateTimeOffset staleObservation = Now - RateBudgetReading.StalenessWindow - TimeSpan.FromSeconds(1);
        RateBudgetReading reading = new RateBudgetReadingBuilder()
            .WithRemaining(0)
            .WithObservedAt(staleObservation)
            .Build();

        // Act
        ProviderBudgetHealth result = ProviderBudgetPolicy.Evaluate(reading, floor: 500, now: Now);

        // Assert
        result.ShouldBe(ProviderBudgetHealth.Unknown);
    }

    [Fact]
    public void WhenReadingIsStale_NeverReturnsLow()
    {
        // Arrange — a stale reading with 0 remaining must degrade to Unknown, not Low
        DateTimeOffset staleObservation = Now - RateBudgetReading.StalenessWindow - TimeSpan.FromSeconds(1);
        RateBudgetReading reading = new RateBudgetReadingBuilder()
            .WithRemaining(0)
            .WithObservedAt(staleObservation)
            .Build();

        // Act
        ProviderBudgetHealth result = ProviderBudgetPolicy.Evaluate(reading, floor: 500, now: Now);

        // Assert
        result.ShouldNotBe(ProviderBudgetHealth.Low);
    }
}
