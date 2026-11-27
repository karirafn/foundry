using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Shared;

using WorkerRunCompletedEvent = Foundry.Modules.Workers.Domain.Events.WorkerRunCompleted;
using WorkerRunFailedEvent = Foundry.Modules.Workers.Domain.Events.WorkerRunFailed;
using WorkerRunStartedEvent = Foundry.Modules.Workers.Domain.Events.WorkerRunStarted;

namespace Foundry.Modules.Workers.Domain.Entities.States;

public sealed class StartingRun : WorkerRun
{
    // Private parameterless constructor for EF Core materialization.
    private StartingRun()
    {
    }

    private StartingRun(WorkerRunId id, IssueId issueId, DateTimeOffset createdAt, WorkerRole role, string? image)
        : base(id, issueId, createdAt, role, image)
    {
    }

    /// <summary>
    /// Begins a run. <paramref name="role"/> defaults to <see cref="WorkerRole.Implement"/>; <paramref name="image"/> is the
    /// worker image the run is dispatched on (see <see cref="WorkerRun.Image"/>). <paramref name="refusesNeedsInput"/> marks a run started
    /// by Continue without answer (see <see cref="WorkerRun.RefusesNeedsInput"/>). <paramref name="claim"/> is the claim the
    /// run is started from, kept so the run starts outside the event that claimed it.
    /// </summary>
    /// <remarks>
    /// Every run mints a Run Source Token (ADR 0112): the run persists only its hash, and the token itself is readable
    /// from <see cref="SourceToken"/> on the begun run alone.
    /// </remarks>
    public static StartingRun Begin(
        IssueId issueId,
        WorkerRunId workerRunId,
        WorkerRole? role = null,
        string? image = null,
        bool refusesNeedsInput = false,
        RunModel? runModel = null,
        ClaimedIssueDispatch? claim = null)
    {
        RunSourceToken sourceToken = RunSourceToken.Mint();
        StartingRun starting = new(workerRunId, issueId, DateTimeOffset.UtcNow, role ?? WorkerRole.Implement, image)
        {
            SourceToken = sourceToken,
            Claim = claim,
        };
        starting.StoreSourceTokenHash(sourceToken.Hash);
        if (runModel is not null)
        {
            starting.StoreRunModel(runModel);
        }
        if (refusesNeedsInput)
        {
            starting.RefuseNeedsInput();
        }

        return starting;
    }

    /// <summary>
    /// The run's Run Source Token, for its worker. Set only on the run <see cref="Begin"/> returned: it is never
    /// persisted, so a run loaded from the database has none.
    /// </summary>
    public RunSourceToken? SourceToken { get; private init; }

    /// <summary>
    /// The claim the run was begun from; <see langword="null"/> for a run begun without one, which is never started
    /// from it.
    /// </summary>
    public ClaimedIssueDispatch? Claim { get; private init; }

    /// <summary>
    /// When starting the run from its <see cref="Claim"/> was attempted; <see langword="null"/> until then. A start is
    /// attempted at most once, so an interrupted start is left to fail as stale rather than started twice.
    /// </summary>
    public DateTimeOffset? StartAttemptedAt { get; private set; }

    /// <summary>
    /// Marks the start attempted at <paramref name="attemptedAt"/> and mints the run's Run Source Token for it (ADR 0112):
    /// a run loaded from the database has no token, so the start gets a fresh one and the run keeps only its hash.
    /// </summary>
    public RunSourceToken BeginStartAttempt(DateTimeOffset attemptedAt)
    {
        if (Claim is null)
        {
            throw new InvalidOperationException($"Worker run {Id} has no claim to start from.");
        }

        if (StartAttemptedAt is not null)
        {
            throw new InvalidOperationException($"Starting worker run {Id} was already attempted.");
        }

        RunSourceToken sourceToken = RunSourceToken.Mint();
        StoreSourceTokenHash(sourceToken.Hash);
        StartAttemptedAt = attemptedAt;
        return sourceToken;
    }

    /// <summary>
    /// Records the base the Publisher prepared for the run (ADR 0100) from <paramref name="repositoryId"/>; later states
    /// carry both.
    /// </summary>
    public void RecordPreparedBase(RunBase runBase, MonitoredRepositoryId repositoryId)
    {
        ArgumentNullException.ThrowIfNull(runBase);
        RecordBase(runBase, repositoryId);
    }

    /// <summary>Records the open issues Foundry supplied in the run's Tracker Context before prompt fitting; later states carry them.</summary>
    public void RecordSuppliedOpenIssues(TrackerContext trackerContext)
    {
        ArgumentNullException.ThrowIfNull(trackerContext);

        IEnumerable<int> openReferencedNumbers = trackerContext.ReferencedIssues
            .Where(referenced => referenced.IsOpen)
            .Select(referenced => referenced.Number);
        IEnumerable<int> indexNumbers = (trackerContext.OpenIssues ?? [])
            .Select(openIssue => openIssue.Number);

        StoreSuppliedOpenIssueNumbers([.. openReferencedNumbers.Concat(indexNumbers).Distinct()]);
    }

    /// <summary>Records the fingerprint of the spec the issue body supplied to the run states; later states carry it.</summary>
    public void RecordSuppliedSpecHash(SpecHash specHash)
    {
        ArgumentNullException.ThrowIfNull(specHash);
        StoreSuppliedSpecHash(specHash);
    }

    /// <summary>
    /// Records the facts code computed for a review run (ADR 0114): its pull request, reviewed commit and patch-id, its
    /// classified changed paths and the flag over them, and the issue's acceptance criteria. Later states carry them, so
    /// the verdict is checked against the same facts after a restart.
    /// </summary>
    public void RecordSuppliedReviewFacts(SuppliedReviewFacts suppliedReviewFacts)
    {
        ArgumentNullException.ThrowIfNull(suppliedReviewFacts);
        StoreSuppliedReviewFacts(suppliedReviewFacts);
    }

    public ActiveRun Activate(
        ContainerId containerId,
        BranchName? branchName,
        MonitoredRepositoryId monitoredRepositoryId)
    {
        ActiveRun active = ActiveRun.FromStarting(this, containerId, branchName, monitoredRepositoryId);
        AddDomainEvent(new WorkerRunStartedEvent(Id, IssueId));
        return active;
    }

    /// <summary>
    /// Completes the review without starting a container (ADR 0113): the change it was dispatched to review is unchanged
    /// since an earlier approval, so <paramref name="carriedVerdict"/> is that approval carried to the new head.
    /// </summary>
    public CompletedRun CompleteCarried(RecordedReviewVerdict carriedVerdict)
    {
        ArgumentNullException.ThrowIfNull(carriedVerdict);
        if (carriedVerdict.CarriedFrom is null || SuppliedReviewFacts is null)
        {
            throw new InvalidOperationException("Only a review run with its supplied facts completes with a carried approval.");
        }

        CompletedRun completed = CompletedRun.FromStarting(this, carriedVerdict);
        AddDomainEvent(new WorkerRunCompletedEvent(Id, IssueId, BranchName: null, PullRequestUrl: null));
        return completed;
    }

    public FailedRun Fail(FailureReason reason)
    {
        FailedRun failed = FailedRun.FromStarting(this, reason);
        AddDomainEvent(new WorkerRunFailedEvent(Id, IssueId, reason.Summary, reason.CategoryToken, BranchName: null));
        return failed;
    }
}
