using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;

namespace Foundry.Modules.Issues.Domain.Entities;

/// <summary>
/// Intermediate abstract base for issue states that can be claimed by a worker.
/// Carries the polymorphic dispatch members shared by all queued variants.
/// </summary>
public abstract class QueuedIssue : Issue
{
    // Private parameterless constructor for EF Core materialization.
    private protected QueuedIssue() : base(IssueId.New())
    {
    }

    protected QueuedIssue(IssueId id) : base(id)
    {
    }

    /// <summary>
    /// Priority rank used for dispatch ordering. Lower rank = higher priority.
    /// </summary>
    public abstract int TierRank { get; }

    /// <summary>
    /// The branch name the worker should operate on.
    /// </summary>
    public abstract BranchName DispatchBranchName { get; }

    /// <summary>
    /// The dispatch context describing the nature of the work (fresh, revision, or continuation).
    /// </summary>
    public abstract DispatchContext Context { get; }

    /// <summary>
    /// Transitions the issue to its in-progress state, assigning the given worker run.
    /// </summary>
    public abstract Issue Claim(WorkerRunId workerRunId);
}
