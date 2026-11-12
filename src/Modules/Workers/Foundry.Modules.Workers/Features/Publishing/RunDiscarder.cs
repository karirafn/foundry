using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Features.Orchestration;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;

using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Workers.Features.Publishing;

/// <summary>
/// Ends an unpublished run without publishing it: the run becomes <paramref name="discarded"/> with its Run Transcript,
/// then its Hand-off volume, the only copy of its unpublished commits, is deleted.
/// </summary>
internal static class RunDiscarder
{
    public static async Task DiscardAsync(
        DbContext dbContext,
        IWorkerOrchestrator orchestrator,
        IDomainEventDispatcher domainEventDispatcher,
        UnpublishedRun run,
        FailedRun discarded,
        CancellationToken cancellationToken)
    {
        WorkerRunTranscript? transcript = run.TranscriptForTerminalState();
        await dbContext.TransitionAsync(
            run,
            discarded,
            transcript is null ? [] : [transcript],
            domainEventDispatcher,
            cancellationToken);
        await orchestrator.DeleteHandOffVolumeAsync(run.Id, cancellationToken);
    }
}
