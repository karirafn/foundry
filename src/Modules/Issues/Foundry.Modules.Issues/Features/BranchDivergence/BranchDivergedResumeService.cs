using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Monitoring.Contracts.Commands;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Issues.Features.BranchDivergence;

/// <summary>
/// The automatic exit from <c>branch_diverged</c> (ADR 0100): once a person merges the side branch into the issue
/// branch, the side branch's tip is an ancestor of the issue branch's head, and the issue resumes as a continuation on
/// the issue branch, the same transition Retry Issue makes. Anything short of a confirmed merge leaves the issue as it is.
/// </summary>
internal sealed class BranchDivergedResumeService : PeriodicBackgroundService
{
    internal static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BranchDivergedResumeService> _log;
    private readonly TimeProvider _timeProvider;

    // Explicit constructor required — PeriodicBackgroundService has a protected constructor,
    // so primary constructors are not available here.
    public BranchDivergedResumeService(
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        ILogger<BranchDivergedResumeService> logger) : base(logger)
    {
        _scopeFactory = scopeFactory;
        _log = logger;
        _timeProvider = timeProvider;
    }

    protected override TimeSpan TickInterval => CheckInterval;

    protected override string ServiceName => nameof(BranchDivergedResumeService);

    internal Task TickForTest(CancellationToken cancellationToken) => TickAsync(cancellationToken);

    protected override async Task TickAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        DbContext db = scope.ServiceProvider.GetRequiredService<DbContext>();
        IDomainEventDispatcher domainEventDispatcher = scope.ServiceProvider.GetRequiredService<IDomainEventDispatcher>();
        IRunPublishingCommands publishing = scope.ServiceProvider.GetRequiredService<IRunPublishingCommands>();

        List<BranchDivergedIssue> diverged = await db.Set<BranchDivergedIssue>()
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        foreach (BranchDivergedIssue candidate in diverged)
        {
            await TryResumeAsync(db, domainEventDispatcher, publishing, candidate, cancellationToken);
        }
    }

    private async Task TryResumeAsync(
        DbContext db,
        IDomainEventDispatcher domainEventDispatcher,
        IRunPublishingCommands publishing,
        BranchDivergedIssue candidate,
        CancellationToken cancellationToken)
    {
        try
        {
            Result<bool> merged = await publishing.IsAncestorOfBranchAsync(
                candidate.MonitoredRepositoryId,
                candidate.SideBranchName,
                candidate.BranchName,
                cancellationToken);

            if (merged is not Result<bool>.Success { Value: true })
            {
                return;
            }

            // Reloaded tracked: the candidate was read untracked, and an operator's Retry Issue may have moved it since.
            Issue? live = await db.Set<Issue>()
                .FirstOrDefaultAsync(i => i.Id == candidate.Id, cancellationToken);

            if (live is not BranchDivergedIssue liveDiverged || liveDiverged.WorkerRunId != candidate.WorkerRunId)
            {
                return;
            }

            ContinuationQueuedIssue queued = liveDiverged.Retry(_timeProvider.GetUtcNow());
            await db.TransitionAsync(liveDiverged, queued, domainEventDispatcher, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // A concurrent Retry Issue won the race; nothing is left to do.
        }
#pragma warning disable CA1031 // One issue's failed check must not stop the others; the next tick checks it again.
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            _log.LogWarning(
                ex,
                "Checking whether the side branch of diverged issue {IssueId} was merged failed.",
                candidate.Id.Value);
        }
    }
}
