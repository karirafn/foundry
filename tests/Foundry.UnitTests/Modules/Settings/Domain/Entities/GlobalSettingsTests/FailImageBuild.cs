using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Modules.Settings.Domain.ValueObjects;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Domain.Entities.GlobalSettingsTests;

public sealed class FailImageBuild
{
    [Fact]
    public void WhenCalled_SetsStateToFailedRecord()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        settings.BeginImageBuild();

        // Act
        settings.FailImageBuild("error log tail", nextRetryAt: null, attempt: 0);

        // Assert
        settings.ImageBuildState.ShouldBeOfType<ImageBuildState.Failed>();
    }

    [Fact]
    public void WhenCalled_StoresErrorTailOnFailedState()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        settings.BeginImageBuild();
        const string errorTail = "Step 5/10 : RUN apt-get install dotnet\nERROR: package not found";

        // Act
        settings.FailImageBuild(errorTail, nextRetryAt: null, attempt: 0);

        // Assert
        ImageBuildState.Failed failed = settings.ImageBuildState.ShouldBeOfType<ImageBuildState.Failed>();
        failed.ErrorTail.ShouldBe(errorTail);
    }

    [Fact]
    public void WhenCalledWithNullErrorTail_ErrorTailIsNull()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        settings.BeginImageBuild();

        // Act
        settings.FailImageBuild(null, nextRetryAt: null, attempt: 0);

        // Assert
        ImageBuildState.Failed failed = settings.ImageBuildState.ShouldBeOfType<ImageBuildState.Failed>();
        failed.ErrorTail.ShouldBeNull();
    }

    [Fact]
    public void WhenCalled_UpdatesUpdatedAt()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        settings.BeginImageBuild();
        DateTimeOffset before = settings.UpdatedAt;

        // Act
        settings.FailImageBuild("error", nextRetryAt: null, attempt: 0);

        // Assert
        settings.UpdatedAt.ShouldBeGreaterThanOrEqualTo(before);
    }

    [Fact]
    public void WhenCalledAfterSuccessfulBuild_LastImageBuiltAtIsPreserved()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        settings.BeginImageBuild();
        settings.CompleteImageBuild();
        DateTimeOffset? lastBuiltAt = settings.LastImageBuiltAt;

        // Act
        settings.BeginImageBuild();
        settings.FailImageBuild("error after prior success", nextRetryAt: null, attempt: 0);

        // Assert
        settings.LastImageBuiltAt.ShouldBe(lastBuiltAt);
    }

    [Fact]
    public void WhenCalledWithNextRetryAt_StoresNextRetryAtOnFailedState()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        settings.BeginImageBuild();
        DateTimeOffset nextRetryAt = DateTimeOffset.UtcNow.AddSeconds(30);

        // Act
        settings.FailImageBuild("error", nextRetryAt, attempt: 1);

        // Assert
        ImageBuildState.Failed failed = settings.ImageBuildState.ShouldBeOfType<ImageBuildState.Failed>();
        failed.NextRetryAt.ShouldBe(nextRetryAt);
    }

    [Fact]
    public void WhenCalledWithAttempt_StoresAttemptOnFailedState()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        settings.BeginImageBuild();

        // Act
        settings.FailImageBuild("error", nextRetryAt: null, attempt: 2);

        // Assert
        ImageBuildState.Failed failed = settings.ImageBuildState.ShouldBeOfType<ImageBuildState.Failed>();
        failed.Attempt.ShouldBe(2);
    }

    [Fact]
    public void WhenCalled_RaisesExactlyOneImageBuildFailedEventWithCorrectPayload()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        settings.BeginImageBuild();
        settings.ClearIntegrationEvents();
        const string errorTail = "Build failed: package not found";
        DateTimeOffset nextRetryAt = DateTimeOffset.UtcNow.AddSeconds(30);
        const int attempt = 1;

        // Act
        settings.FailImageBuild(errorTail, nextRetryAt, attempt);

        // Assert
        IReadOnlyList<IIntegrationEvent> events = settings.IntegrationEvents;
        events.Count.ShouldBe(1);
        ImageBuildFailed evt = events[0].ShouldBeOfType<ImageBuildFailed>();
        evt.ShouldSatisfyAllConditions(
            () => evt.ErrorTail.ShouldBe(errorTail),
            () => evt.NextRetryAt.ShouldBe(nextRetryAt),
            () => evt.Attempt.ShouldBe(attempt));
    }
}
