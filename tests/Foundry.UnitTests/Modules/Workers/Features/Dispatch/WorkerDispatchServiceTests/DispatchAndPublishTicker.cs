using Foundry.Modules.Workers.Features.Dispatch;
using Foundry.Modules.Workers.Features.Publishing;

namespace Foundry.UnitTests.Modules.Workers.Features.Dispatch.WorkerDispatchServiceTests;

/// <summary>
/// Runs a dispatch tick followed by a publishing tick, so a run whose container ended is published and resolved within
/// one test tick, as the two background loops do in production (ADR 0100).
/// </summary>
internal sealed class DispatchAndPublishTicker(WorkerDispatchService dispatch, RunPublishingService publishing)
{
    public WorkerDispatchService Dispatch => dispatch;

    public async Task ExecuteTickAsync(CancellationToken cancellationToken)
    {
        await dispatch.ExecuteTickAsync(cancellationToken);
        await publishing.ExecuteTickAsync(cancellationToken);
    }

    public async Task<bool> ReconcileOrphanedRunsForTestAsync(CancellationToken cancellationToken)
    {
        bool reachable = await dispatch.ReconcileOrphanedRunsForTestAsync(cancellationToken);
        await publishing.ExecuteTickAsync(cancellationToken);
        return reachable;
    }
}
