using Foundry.Shared;

namespace Foundry.UnitTests.Shared.Infrastructure.Outbox.IntegrationEventProcessorTests;

internal sealed record TestDedupEvent(string Name) : IIntegrationEvent;

[IntegrationEventHandlerIdentity("Test.RecordingDedup")]
internal sealed class RecordingDedupEventHandler : IIntegrationEventHandler<TestDedupEvent>
{
    public int InvokeCount { get; private set; }

    public Task HandleAsync(TestDedupEvent @event, CancellationToken cancellationToken)
    {
        InvokeCount++;
        return Task.CompletedTask;
    }
}

/// <summary>
/// A distinct second handler type with its own stable dedup identity — verifies that
/// two handlers for the same event each produce a distinct <c>processed_events</c> row.
/// </summary>
[IntegrationEventHandlerIdentity("Test.SecondRecordingDedup")]
internal sealed class SecondRecordingDedupEventHandler : IIntegrationEventHandler<TestDedupEvent>
{
    public int InvokeCount { get; private set; }

    public Task HandleAsync(TestDedupEvent @event, CancellationToken cancellationToken)
    {
        InvokeCount++;
        return Task.CompletedTask;
    }
}

[IntegrationEventHandlerIdentity("Test.ThrowingDedup")]
internal sealed class ThrowingDedupEventHandler : IIntegrationEventHandler<TestDedupEvent>
{
    public bool ShouldSucceedOnNextCall { get; set; }

    public int InvokeCount { get; private set; }

    public Task HandleAsync(TestDedupEvent @event, CancellationToken cancellationToken)
    {
        InvokeCount++;

        if (!ShouldSucceedOnNextCall)
        {
            throw new InvalidOperationException("Simulated handler failure.");
        }

        return Task.CompletedTask;
    }
}
