using Foundry.Shared;

namespace Foundry.UnitTests.Shared.Infrastructure.Outbox.IntegrationEventProcessorTests;

internal sealed record TestDedupEvent(string Name) : IIntegrationEvent;

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
/// A distinct second handler type so its <see cref="Type.FullName"/> differs from
/// <see cref="RecordingDedupEventHandler"/> — dedup keys on the handler's full type name.
/// </summary>
internal sealed class SecondRecordingDedupEventHandler : IIntegrationEventHandler<TestDedupEvent>
{
    public int InvokeCount { get; private set; }

    public Task HandleAsync(TestDedupEvent @event, CancellationToken cancellationToken)
    {
        InvokeCount++;
        return Task.CompletedTask;
    }
}

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
