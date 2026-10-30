using Foundry.Modules.Credentials.Contracts;
using Foundry.Modules.Credentials.Features.CredentialReactions;
using Foundry.Modules.Credentials.Features.Usage;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Credentials.Features.CredentialReactions.UsageCredentialsInvalidatedHandlerTests;

public sealed class HandleAsync
{
    // ---------------------------------------------------------------------------
    // Stubs
    // ---------------------------------------------------------------------------

    private sealed class SpyUsageSnapshotStateMutator : IUsageSnapshotStateMutator
    {
        public int ClearCallCount { get; private set; }

        public void SetSnapshot(UsageSnapshot snapshot) { }

        public void MarkRefreshFailed(DateTimeOffset failedAt) { }

        public void Clear() => ClearCallCount++;
    }

    private sealed class SpyUsageSnapshotBroadcaster : IUsageSnapshotBroadcaster
    {
        public int CallCount { get; private set; }
        public UsageSnapshot? LastSnapshot { get; private set; } = new UsageSnapshot([], null, DateTimeOffset.UtcNow, "sentinel@example.com", null);

        public Task SendSnapshotAsync(UsageSnapshot? snapshot, CancellationToken cancellationToken)
        {
            CallCount++;
            LastSnapshot = snapshot;
            return Task.CompletedTask;
        }
    }

    // ---------------------------------------------------------------------------
    // Tests
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task WhenEventReceived_ClearsSnapshot()
    {
        // Arrange
        SpyUsageSnapshotStateMutator mutator = new();
        SpyUsageSnapshotBroadcaster broadcaster = new();
        IIntegrationEventHandler<CredentialsInvalidated> sut =
            new UsageCredentialsInvalidatedHandler(mutator, broadcaster);

        CredentialsInvalidated @event = new("credentials expired");

        // Act
        await sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        mutator.ClearCallCount.ShouldBe(1);
    }

    [Fact]
    public async Task WhenEventReceived_BroadcastsNull()
    {
        // Arrange
        SpyUsageSnapshotStateMutator mutator = new();
        SpyUsageSnapshotBroadcaster broadcaster = new();
        IIntegrationEventHandler<CredentialsInvalidated> sut =
            new UsageCredentialsInvalidatedHandler(mutator, broadcaster);

        CredentialsInvalidated @event = new("credentials expired");

        // Act
        await sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        broadcaster.CallCount.ShouldBe(1);
        broadcaster.LastSnapshot.ShouldBeNull();
    }
}
