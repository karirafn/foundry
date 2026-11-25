using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Modules.Settings.Domain.ValueObjects;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Domain.Entities.GlobalSettingsTests;

public sealed class FailScheduledImageBuild
{
    [Fact]
    public void WhenCalled_RecordsTheFailureAsAScheduledRebuildFailure()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        settings.BeginImageBuild();
        DateTimeOffset nextRetryAt = new(2026, 11, 25, 8, 0, 0, TimeSpan.Zero);

        // Act
        settings.FailScheduledImageBuild("apt failed", nextRetryAt, attempt: 2);

        // Assert
        settings.ImageBuildState.ShouldBe(new ImageBuildState.ScheduledRebuildFailed("apt failed", nextRetryAt, 2));
    }

    [Fact]
    public void WhenCalledAfterASuccessfulBuild_KeepsLastImageBuiltAt()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        settings.BeginImageBuild();
        settings.CompleteImageBuild();
        DateTimeOffset? lastBuiltAt = settings.LastImageBuiltAt;
        settings.BeginImageBuild();

        // Act
        settings.FailScheduledImageBuild("apt failed", nextRetryAt: null, attempt: 1);

        // Assert
        settings.LastImageBuiltAt.ShouldBe(lastBuiltAt);
    }

    [Fact]
    public void WhenCalled_RaisesOnlyAScheduledImageBuildFailedEvent()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        settings.BeginImageBuild();
        settings.ClearIntegrationEvents();
        DateTimeOffset nextRetryAt = new(2026, 11, 25, 8, 0, 0, TimeSpan.Zero);

        // Act
        settings.FailScheduledImageBuild("apt failed", nextRetryAt, attempt: 1);

        // Assert
        IIntegrationEvent single = settings.IntegrationEvents.ShouldHaveSingleItem();
        single.ShouldBe(new ScheduledImageBuildFailed("apt failed", nextRetryAt, 1));
    }
}
