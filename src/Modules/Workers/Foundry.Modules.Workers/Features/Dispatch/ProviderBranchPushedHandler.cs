using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;

namespace Foundry.Modules.Workers.Features.Dispatch;

/// <summary>
/// Records a push to a running worker's branch so the next dispatch tick runs that run's branch compare
/// (issue #660). The compare itself stays in <see cref="WorkerDispatchService"/>, the single writer of
/// run state, which caps it at one compare per run per tick.
/// </summary>
[IntegrationEventHandlerIdentity("Workers.ProviderBranchPushed")]
internal sealed class ProviderBranchPushedHandler(RunningBranchCompareGate gate)
    : IIntegrationEventHandler<ProviderBranchPushed>
{
    public Task HandleAsync(ProviderBranchPushed @event, CancellationToken cancellationToken)
    {
        gate.RecordPush(@event.RepositoryId, @event.Branch);
        return Task.CompletedTask;
    }
}
