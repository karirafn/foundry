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

    /// <summary>
    /// A coordinator whose PollAsync never completes until released — used to verify
    /// that HandleAsync does not block on the poll (fire-and-forget).
    /// </summary>
    private sealed class BlockingUsagePollCoordinator : IUsagePollCoordinator
    {
        private readonly TaskCompletionSource<UsageSnapshot?> _tcs = new();

        public void Release() => _tcs.SetResult(null);

        public Task<UsageSnapshot?> PollAsync(CancellationToken cancellationToken)
            => _tcs.Task;
    }

    [Fact]
    public async Task WhenPollWouldBlock_HandlerReturnsWithoutWaitingForPoll()
    {
        // Arrange — coordinator blocks until released; if HandleAsync awaits the poll, this test hangs.
        SpyUsageSnapshotStateMutator mutator = new();
        BlockingUsagePollCoordinator coordinator = new();
        IIntegrationEventHandler<CredentialsValidated> sut =
            new UsageCredentialsValidatedHandler(mutator, coordinator);

        CredentialsValidated @event = new("alice@example.com", "Acme Corp", "pro");

        // Act — must complete even though the coordinator never resolves
        Task handleTask = sut.HandleAsync(@event, CancellationToken.None);
        Task completed = await Task.WhenAny(handleTask, Task.Delay(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));

        // Assert — handler completed before the 2s timeout (i.e., did not block on poll)
        completed.ShouldBeSameAs(handleTask);

        // Release the background task to avoid dangling tasks after the test.
        coordinator.Release();
    }
}
