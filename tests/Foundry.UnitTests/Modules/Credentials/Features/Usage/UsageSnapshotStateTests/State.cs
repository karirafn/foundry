using Foundry.Modules.Credentials.Features.Usage;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Credentials.Features.Usage.UsageSnapshotStateTests;

public sealed class State
{
    // ---------------------------------------------------------------------------
    // Behavior (a): Current returns null initially
    // ---------------------------------------------------------------------------

    [Fact]
    public void WhenNew_CurrentIsNull()
    {
        // Arrange
        UsageSnapshotState sut = new();

        // Act
        UsageSnapshot? current = ((IUsageSnapshotState)sut).Current;

        // Assert
        current.ShouldBeNull();
    }

    // ---------------------------------------------------------------------------
    // Behavior (b): SetSnapshot stores and Current returns it
    // ---------------------------------------------------------------------------

    [Fact]
    public void WhenSnapshotSet_CurrentReturnsIt()
    {
        // Arrange
        UsageSnapshotState sut = new();
        UsageSnapshot snapshot = new UsageSnapshotBuilder().Build();

        // Act
        ((IUsageSnapshotStateMutator)sut).SetSnapshot(snapshot);
        UsageSnapshot? current = ((IUsageSnapshotState)sut).Current;

        // Assert
        current.ShouldNotBeNull();
        current.AccountEmail.ShouldBe(snapshot.AccountEmail);
        current.CapturedAt.ShouldBe(snapshot.CapturedAt);
    }

    // ---------------------------------------------------------------------------
    // Behavior (c): MarkRefreshFailed retains snapshot but stamps LastRefreshFailedAt
    // ---------------------------------------------------------------------------

    [Fact]
    public void WhenMarkRefreshFailed_StampsLastRefreshFailedAtOnCurrentSnapshot()
    {
        // Arrange
        UsageSnapshotState sut = new();
        UsageSnapshot snapshot = new UsageSnapshotBuilder().Build();
        ((IUsageSnapshotStateMutator)sut).SetSnapshot(snapshot);
        DateTimeOffset failedAt = DateTimeOffset.UtcNow;

        // Act
        ((IUsageSnapshotStateMutator)sut).MarkRefreshFailed("other@example.com", failedAt);
        UsageSnapshot? current = ((IUsageSnapshotState)sut).Current;

        // Assert — the retained snapshot keeps its own account and figures
        current.ShouldNotBeNull();
        current.AccountEmail.ShouldBe(snapshot.AccountEmail);
        current.CapturedAt.ShouldBe(snapshot.CapturedAt);
        current.IsUnavailable.ShouldBeFalse();
        current.LastRefreshFailedAt.ShouldBe(failedAt);
    }

    [Fact]
    public void WhenMarkRefreshFailed_AndNoCurrentSnapshot_StoresUnavailableSnapshot()
    {
        // Arrange
        UsageSnapshotState sut = new();
        DateTimeOffset failedAt = DateTimeOffset.UtcNow;

        // Act
        ((IUsageSnapshotStateMutator)sut).MarkRefreshFailed("user@example.com", failedAt);
        UsageSnapshot? current = ((IUsageSnapshotState)sut).Current;

        // Assert
        current.ShouldNotBeNull();
        current.ShouldSatisfyAllConditions(
            () => current.IsUnavailable.ShouldBeTrue(),
            () => current.CapturedAt.ShouldBeNull(),
            () => current.Limits.ShouldBeEmpty(),
            () => current.ExtraUsage.ShouldBeNull(),
            () => current.AccountEmail.ShouldBe("user@example.com"),
            () => current.LastRefreshFailedAt.ShouldBe(failedAt));
    }

    [Fact]
    public void WhenMarkRefreshFailed_AgainWhileUnavailable_UpdatesFailedAt()
    {
        // Arrange
        UsageSnapshotState sut = new();
        DateTimeOffset later = DateTimeOffset.UtcNow;
        ((IUsageSnapshotStateMutator)sut).MarkRefreshFailed("user@example.com", later.AddMinutes(-5));

        // Act
        ((IUsageSnapshotStateMutator)sut).MarkRefreshFailed("user@example.com", later);
        UsageSnapshot? current = ((IUsageSnapshotState)sut).Current;

        // Assert
        current.ShouldNotBeNull();
        current.IsUnavailable.ShouldBeTrue();
        current.LastRefreshFailedAt.ShouldBe(later);
    }

    // ---------------------------------------------------------------------------
    // Behavior (d): SetSnapshot after failure clears LastRefreshFailedAt
    // ---------------------------------------------------------------------------

    [Fact]
    public void WhenSetSnapshotAfterFailure_ClearsLastRefreshFailedAt()
    {
        // Arrange
        UsageSnapshotState sut = new();
        UsageSnapshot initialSnapshot = new UsageSnapshotBuilder().Build();
        ((IUsageSnapshotStateMutator)sut).SetSnapshot(initialSnapshot);
        ((IUsageSnapshotStateMutator)sut).MarkRefreshFailed("user@example.com", DateTimeOffset.UtcNow);

        UsageSnapshot freshSnapshot = new UsageSnapshotBuilder()
            .WithAccountEmail("other@example.com")
            .Build();

        // Act
        ((IUsageSnapshotStateMutator)sut).SetSnapshot(freshSnapshot);
        UsageSnapshot? current = ((IUsageSnapshotState)sut).Current;

        // Assert
        current.ShouldNotBeNull();
        current.LastRefreshFailedAt.ShouldBeNull();
    }

    // ---------------------------------------------------------------------------
    // Behavior (e): Clear sets Current to null
    // ---------------------------------------------------------------------------

    [Fact]
    public void WhenClear_CurrentBecomesNull()
    {
        // Arrange
        UsageSnapshotState sut = new();
        UsageSnapshot snapshot = new UsageSnapshotBuilder().Build();
        ((IUsageSnapshotStateMutator)sut).SetSnapshot(snapshot);

        // Act
        ((IUsageSnapshotStateMutator)sut).Clear();
        UsageSnapshot? current = ((IUsageSnapshotState)sut).Current;

        // Assert
        current.ShouldBeNull();
    }
}
