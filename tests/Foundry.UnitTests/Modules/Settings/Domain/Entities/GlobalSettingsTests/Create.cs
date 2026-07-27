using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Modules.Settings.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Domain.Entities.GlobalSettingsTests;

public sealed class Create
{
    [Fact]
    public void WhenCreated_HasDefaultId()
    {
        // Arrange & Act
        GlobalSettings settings = GlobalSettings.Create();

        // Assert
        settings.Id.ShouldBe(GlobalSettingsId.Default);
    }

    [Fact]
    public void WhenCreated_HasDefaultMaxConcurrent()
    {
        // Arrange & Act
        GlobalSettings settings = GlobalSettings.Create();

        // Assert
        settings.MaxConcurrent.ShouldBe(1);
    }

    [Fact]
    public void WhenCreated_HasDefaultTimeoutMinutes()
    {
        // Arrange & Act
        GlobalSettings settings = GlobalSettings.Create();

        // Assert
        settings.TimeoutMinutes.ShouldBe(120);
    }

    [Fact]
    public void WhenCreated_CreatedAtIsSet()
    {
        // Arrange
        DateTimeOffset before = DateTimeOffset.UtcNow;

        // Act
        GlobalSettings settings = GlobalSettings.Create();

        // Assert
        DateTimeOffset after = DateTimeOffset.UtcNow;
        settings.CreatedAt.ShouldBeInRange(before, after);
    }

    [Fact]
    public void WhenCreated_UpdatedAtMatchesCreatedAt()
    {
        // Arrange & Act
        GlobalSettings settings = GlobalSettings.Create();

        // Assert
        settings.UpdatedAt.ShouldBe(settings.CreatedAt);
    }

    [Fact]
    public void WhenCreated_UsageLimitResetsAtIsNull()
    {
        // Arrange & Act
        GlobalSettings settings = GlobalSettings.Create();

        // Assert
        settings.UsageLimitResetsAt.ShouldBeNull();
    }

    [Fact]
    public void WhenCreated_IsDispatchPausedIsFalse()
    {
        // Arrange & Act
        GlobalSettings settings = GlobalSettings.Create();

        // Assert
        settings.IsDispatchPaused.ShouldBeFalse();
    }

    [Fact]
    public void WhenCreated_AutoResumeOnUsageResetIsTrue()
    {
        // Arrange & Act
        GlobalSettings settings = GlobalSettings.Create();

        // Assert
        settings.AutoResumeOnUsageReset.ShouldBeTrue();
    }

    [Fact]
    public void WhenCreated_DefaultCooldownMinutesIs60()
    {
        // Arrange & Act
        GlobalSettings settings = GlobalSettings.Create();

        // Assert
        settings.DefaultCooldownMinutes.ShouldBe(60);
    }
}
