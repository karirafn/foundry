using Foundry.Modules.Credentials.Contracts;
using Foundry.Modules.Credentials.Features.CredentialReactions;
using Foundry.Modules.Credentials.Features.Usage;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Credentials.Features.CredentialReactions.UsageCredentialsValidatedHandlerTests;

public sealed class HandleAsync
{
    // ---------------------------------------------------------------------------
    // Stubs
    // ---------------------------------------------------------------------------

    private sealed class StubUsagePollCoordinator : IUsagePollCoordinator
    {
        public int CallCount { get; private set; }
        public UsageSnapshot? ResultSnapshot { get; set; }

        public Task<UsageSnapshot?> PollAsync(CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(ResultSnapshot);
        }
    }

    private sealed class SpyUsageSnapshotStateMutator : IUsageSnapshotStateMutator
    {
        public int ClearCallCount { get; private set; }

        public void SetSnapshot(UsageSnapshot snapshot) { }

        public void MarkRefreshFailed(DateTimeOffset failedAt) { }

        public void Clear() => ClearCallCount++;
    }

    // ---------------------------------------------------------------------------
    // Tests
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task WhenEventReceived_ClearsSnapshot()
    {
        // Arrange
        SpyUsageSnapshotStateMutator mutator = new();
        StubUsagePollCoordinator coordinator = new();
        IIntegrationEventHandler<CredentialsValidated> sut =
            new UsageCredentialsValidatedHandler(mutator, coordinator);

        CredentialsValidated @event = new("alice@example.com", "Acme Corp", "pro");

        // Act
        await sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        mutator.ClearCallCount.ShouldBe(1);
    }

    [Fact]
    public async Task WhenEventReceived_TriggersPoll()
    {
        // Arrange
        SpyUsageSnapshotStateMutator mutator = new();
        StubUsagePollCoordinator coordinator = new();
        IIntegrationEventHandler<CredentialsValidated> sut =
            new UsageCredentialsValidatedHandler(mutator, coordinator);

        CredentialsValidated @event = new("alice@example.com", "Acme Corp", "pro");

        // Act
        await sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        coordinator.CallCount.ShouldBe(1);
    }

    [Fact]
    public async Task WhenEventReceived_ClearsBeforePolling()
    {
        // Arrange — verify clear happens before poll by checking order via a sequenced spy
        List<string> order = [];
        SequencedSpy mutator = new(order);
        SequencedCoordinator coordinator = new(order);
        IIntegrationEventHandler<CredentialsValidated> sut =
            new UsageCredentialsValidatedHandler(mutator, coordinator);

        CredentialsValidated @event = new("alice@example.com", "Acme Corp", "pro");

        // Act
        await sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        order.ShouldBe(["clear", "poll"]);
    }

    private sealed class SequencedSpy(List<string> order) : IUsageSnapshotStateMutator
    {
        public void SetSnapshot(UsageSnapshot snapshot) { }
        public void MarkRefreshFailed(DateTimeOffset failedAt) { }
        public void Clear() => order.Add("clear");
    }

    private sealed class SequencedCoordinator(List<string> order) : IUsagePollCoordinator
    {
        public Task<UsageSnapshot?> PollAsync(CancellationToken cancellationToken)
        {
            order.Add("poll");
            return Task.FromResult<UsageSnapshot?>(null);
        }
    }
}
