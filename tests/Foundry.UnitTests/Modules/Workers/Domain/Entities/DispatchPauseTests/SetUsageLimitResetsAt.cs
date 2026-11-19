using Foundry.Modules.Workers.Domain.Entities;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Domain.Entities.DispatchPauseTests;

public sealed class SetUsageLimitResetsAt
{
    [Fact]
    public void WhenResetsAtIsFuture_SetsUsageLimitResetsAt()
    {
        // Arrange
        DispatchPause pause = DispatchPause.Create();
        DateTimeOffset futureTime = DateTimeOffset.UtcNow.AddHours(2);

        // Act
        pause.SetUsageLimitResetsAt(futureTime);

        // Assert
        pause.UsageLimitResetsAt.ShouldBe(futureTime);
    }

    [Fact]
    public void WhenResetsAtIsFuture_UpdatesUpdatedAt()
    {
        // Arrange
        DispatchPause pause = DispatchPause.Create();
        DateTimeOffset before = pause.UpdatedAt;

        // Act
        pause.SetUsageLimitResetsAt(DateTimeOffset.UtcNow.AddHours(1));

        // Assert
        pause.UpdatedAt.ShouldBeGreaterThanOrEqualTo(before);
    }

    [Fact]
    public void WhenResetsAtIsInPast_DoesNotSetUsageLimitResetsAt()
    {
        // Arrange
        DispatchPause pause = DispatchPause.Create();
        DateTimeOffset pastTime = DateTimeOffset.UtcNow.AddHours(-1);

        // Act
        pause.SetUsageLimitResetsAt(pastTime);

        // Assert
        pause.UsageLimitResetsAt.ShouldBeNull();
    }

    [Fact]
    public void WhenResetsAtIsNow_DoesNotSetUsageLimitResetsAt()
    {
        // Arrange
        DispatchPause pause = DispatchPause.Create();
        DateTimeOffset now = DateTimeOffset.UtcNow;

        // Act
        pause.SetUsageLimitResetsAt(now);

        // Assert
        pause.UsageLimitResetsAt.ShouldBeNull();
    }

    [Fact]
    public void WhenNewResetsAtIsLaterThanExisting_UpdatesToNewValue()
    {
        // Arrange
        DispatchPause pause = DispatchPause.Create();
        DateTimeOffset earlier = DateTimeOffset.UtcNow.AddHours(1);
        DateTimeOffset later = DateTimeOffset.UtcNow.AddHours(3);
        pause.SetUsageLimitResetsAt(earlier);

        // Act
        pause.SetUsageLimitResetsAt(later);

        // Assert
        pause.UsageLimitResetsAt.ShouldBe(later);
    }

    [Fact]
    public void WhenNewResetsAtIsEarlierThanExisting_KeepsExistingValue()
    {
        // Arrange
        DispatchPause pause = DispatchPause.Create();
        DateTimeOffset later = DateTimeOffset.UtcNow.AddHours(3);
        DateTimeOffset earlier = DateTimeOffset.UtcNow.AddHours(1);
        pause.SetUsageLimitResetsAt(later);

        // Act
        pause.SetUsageLimitResetsAt(earlier);

        // Assert
        pause.UsageLimitResetsAt.ShouldBe(later);
    }

    [Fact]
    public void WhenResetsAtExceedsSevenDays_ClampsToSevenDays()
    {
        // Arrange
        DispatchPause pause = DispatchPause.Create();
        DateTimeOffset farFuture = DateTimeOffset.UtcNow.AddDays(30);
        DateTimeOffset before = DateTimeOffset.UtcNow;

        // Act
        pause.SetUsageLimitResetsAt(farFuture);

        // Assert
        DateTimeOffset maxAllowed = before.AddDays(7);
        pause.UsageLimitResetsAt.ShouldNotBeNull();
        pause.UsageLimitResetsAt!.Value.ShouldBeLessThanOrEqualTo(maxAllowed.AddSeconds(1));
        pause.UsageLimitResetsAt!.Value.ShouldBeGreaterThanOrEqualTo(before.AddDays(7).AddSeconds(-1));
    }

    [Fact]
    public void WhenResetsAtIsWithinSevenDays_AcceptsAsIs()
    {
        // Arrange
        DispatchPause pause = DispatchPause.Create();
        DateTimeOffset withinLimit = DateTimeOffset.UtcNow.AddDays(3);

        // Act
        pause.SetUsageLimitResetsAt(withinLimit);

        // Assert
        pause.UsageLimitResetsAt.ShouldBe(withinLimit);
    }
}
