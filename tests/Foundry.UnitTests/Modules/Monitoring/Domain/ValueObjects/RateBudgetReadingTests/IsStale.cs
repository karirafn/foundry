using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.ValueObjects.RateBudgetReadingTests;

public sealed class IsStale
{
    private static readonly DateTimeOffset Now = new(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void WhenObservedJustInsideStalenessWindow_ReturnsFalse()
    {
        // Arrange — one second inside the staleness window boundary
        DateTimeOffset observedAt = Now - RateBudgetReading.StalenessWindow + TimeSpan.FromSeconds(1);
        RateBudgetReading reading = new RateBudgetReadingBuilder()
            .WithObservedAt(observedAt)
            .Build();

        // Act
        bool result = reading.IsStale(Now);

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public void WhenObservedJustOutsideStalenessWindow_ReturnsTrue()
    {
        // Arrange — one second past the staleness window boundary
        DateTimeOffset observedAt = Now - RateBudgetReading.StalenessWindow - TimeSpan.FromSeconds(1);
        RateBudgetReading reading = new RateBudgetReadingBuilder()
            .WithObservedAt(observedAt)
            .Build();

        // Act
        bool result = reading.IsStale(Now);

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public void WhenObservedExactlyAtStalenessWindowBoundary_ReturnsFalse()
    {
        // Arrange — exactly at the boundary (not yet stale)
        DateTimeOffset observedAt = Now - RateBudgetReading.StalenessWindow;
        RateBudgetReading reading = new RateBudgetReadingBuilder()
            .WithObservedAt(observedAt)
            .Build();

        // Act
        bool result = reading.IsStale(Now);

        // Assert
        result.ShouldBeFalse();
    }
}
