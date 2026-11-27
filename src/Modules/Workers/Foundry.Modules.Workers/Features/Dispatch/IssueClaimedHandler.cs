using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Settings.Contracts.Queries;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Roles;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Workers.Features.Dispatch;

/// <summary>
/// Turns a claim into a committed <see cref="StartingRun"/> that carries it, then signals <see cref="WorkerRunStarter"/>.
/// The slow start (issue reads, the Publisher's fetch, the container) runs there, so the outbox relay that delivers this
/// event is never held by it.
/// </summary>
[IntegrationEventHandlerIdentity("Workers.IssueClaimed")]
internal sealed class IssueClaimedHandler(
    DbContext dbContext,
    IGlobalSettingsQueries settingsQueries,
    IWorkerRoleCatalogStore roleCatalog,
    WorkerRunStartSignal startSignal,
    ILogger<IssueClaimedHandler> logger) : IIntegrationEventHandler<IssueClaimed>
{
    public async Task HandleAsync(IssueClaimed @event, CancellationToken cancellationToken)
    {
        ClaimedIssueDispatch claimed = @event.Dispatch;

        // A crash after the save redelivers this event with the run already persisted (in any state). Re-adding it would
        // violate the primary key on every redelivery. A run stranded in Starting is failed by StaleStartingRunService,
        // which gives the issue its way out.
        bool runAlreadyExists = await dbContext.Set<WorkerRun>()
            .AnyAsync(run => run.Id == claimed.WorkerRunId, cancellationToken);

        if (runAlreadyExists)
        {
            logger.LogInformation(
                "Worker run {WorkerRunId} for issue #{IssueNumber} already exists; skipping redelivered claim. "
                + "StaleStartingRunService reconciles a stranded starting run.",
                claimed.WorkerRunId,
                claimed.IssueNumber);
            return;
        }

        // A3: load-then-remove so an absent reservation is a clean no-op (redelivery / swept).
        // The remove stages into the same SaveChangesAsync as the StartingRun add,
        // so reservation delete and run insert commit in one transaction.
        DispatchReservation? reservation = await dbContext.Set<DispatchReservation>()
            .FindAsync([claimed.WorkerRunId], cancellationToken);

        if (reservation is not null)
        {
            dbContext.Set<DispatchReservation>().Remove(reservation);
        }

        // The run's role comes from the queued state's dispatch context, never from a constant here.
        WorkerRole role = DispatchRoleMapping.ToWorkerRole(claimed.Context.Role);
        string image = roleCatalog.Current?.Image.Reference ?? await settingsQueries.GetWorkerImageTagAsync(cancellationToken);
        RunModel runModel = await GetRunModelAsync(role, claimed.Demand, cancellationToken);

        StartingRun startingRun = StartingRun.Begin(
            claimed.IssueId,
            claimed.WorkerRunId,
            role,
            image,
            refusesNeedsInput: claimed.Context.ContinuedWithoutAnswer,
            runModel: runModel,
            claim: claimed);
        dbContext.Set<WorkerRun>().Add(startingRun);
        await dbContext.SaveChangesAsync(cancellationToken);

        startSignal.Signal();
    }

    /// <summary>
    /// The run's demand, model and effort (ADR 0095): an implement run's effective demand, a claim with no demand
    /// reading as demanding, with the Tier Model of that demand; any other role's model from its role setting, with no
    /// demand or effort. Model and effort are empty when never set, so the CLI's account default applies.
    /// </summary>
    private async Task<RunModel> GetRunModelAsync(
        WorkerRole role,
        ImplementDemand? demand,
        CancellationToken cancellationToken)
    {
        if (role == WorkerRole.Implement)
        {
            ImplementDemand effectiveDemand = demand ?? ImplementDemand.Demanding;
            ImplementTierSettings? tier = await dbContext.Set<ImplementTierSettings>()
                .AsNoTracking()
                .FirstOrDefaultAsync(candidate => candidate.Demand == effectiveDemand, cancellationToken);

            return new RunModel(effectiveDemand, tier?.Model ?? string.Empty, tier?.Effort ?? string.Empty);
        }

        WorkerRoleSettings? settings = await dbContext.Set<WorkerRoleSettings>()
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Role == role, cancellationToken);

        return new RunModel(demand: null, settings?.Model ?? string.Empty, string.Empty);
    }
}
