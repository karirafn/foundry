using Foundry.Modules.Settings.Domain.Entities;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Domain.Entities.GlobalSettingsTests;

public sealed class SetUsageLimitResetsAt
{
    [Fact]
    public void WhenResetsAtIsFuture_SetsUsageLimitResetsAt()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        DateTimeOffset futureTime = DateTimeOffset.UtcNow.AddHours(2);

        // Act
        settings.SetUsageLimitResetsAt(futureTime);

        // Assert
        settings.UsageLimitResetsAt.ShouldBe(futureTime);
    }

    [Fact]
    public void WhenResetsAtIsFuture_UpdatesUpdatedAt()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        DateTimeOffset before = settings.UpdatedAt;

        // Act
        settings.SetUsageLimitResetsAt(DateTimeOffset.UtcNow.AddHours(1));

        // Assert
        settings.UpdatedAt.ShouldBeGreaterThanOrEqualTo(before);
    }

    [Fact]
    public void WhenResetsAtIsInPast_DoesNotSetUsageLimitResetsAt()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        DateTimeOffset pastTime = DateTimeOffset.UtcNow.AddHours(-1);

        // Act
        settings.SetUsageLimitResetsAt(pastTime);

        // Assert
        settings.UsageLimitResetsAt.ShouldBeNull();
    }

    [Fact]
    public void WhenResetsAtIsNow_DoesNotSetUsageLimitResetsAt()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        DateTimeOffset now = DateTimeOffset.UtcNow;

        // Act
        settings.SetUsageLimitResetsAt(now);

        // Assert
        settings.UsageLimitResetsAt.ShouldBeNull();
    }

    [Fact]
    public void WhenNewResetsAtIsLaterThanExisting_UpdatesToNewValue()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        DateTimeOffset earlier = DateTimeOffset.UtcNow.AddHours(1);
        DateTimeOffset later = DateTimeOffset.UtcNow.AddHours(3);
        settings.SetUsageLimitResetsAt(earlier);

        // Act
        settings.SetUsageLimitResetsAt(later);

        // Assert
        settings.UsageLimitResetsAt.ShouldBe(later);
    }

    [Fact]
    public void WhenNewResetsAtIsEarlierThanExisting_KeepsExistingValue()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        DateTimeOffset later = DateTimeOffset.UtcNow.AddHours(3);
        DateTimeOffset earlier = DateTimeOffset.UtcNow.AddHours(1);
        settings.SetUsageLimitResetsAt(later);

        // Act
        settings.SetUsageLimitResetsAt(earlier);

        // Assert
        settings.UsageLimitResetsAt.ShouldBe(later);
    }

    [Fact]
    public void WhenResetsAtExceedsSevenDays_ClampsToSevenDays()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        DateTimeOffset farFuture = DateTimeOffset.UtcNow.AddDays(30);
        DateTimeOffset before = DateTimeOffset.UtcNow;

        // Act
        settings.SetUsageLimitResetsAt(farFuture);

        // Assert
        DateTimeOffset maxAllowed = before.AddDays(7);
        settings.UsageLimitResetsAt.ShouldNotBeNull();
        settings.UsageLimitResetsAt!.Value.ShouldBeLessThanOrEqualTo(maxAllowed.AddSeconds(1));
        settings.UsageLimitResetsAt!.Value.ShouldBeGreaterThanOrEqualTo(before.AddDays(7).AddSeconds(-1));
    }

    [Fact]
    public void WhenResetsAtIsWithinSevenDays_AcceptsAsIs()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        DateTimeOffset withinLimit = DateTimeOffset.UtcNow.AddDays(3);

        // Act
        settings.SetUsageLimitResetsAt(withinLimit);

        // Assert
        settings.UsageLimitResetsAt.ShouldBe(withinLimit);
    }
}
