using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Shared;

namespace Foundry.Modules.Workers.Domain.Entities;

public abstract class WorkerRun : AggregateRoot<WorkerRunId>, IStateMachine<WorkerRun>
{
    // Private parameterless constructor for EF Core materialization.
    protected WorkerRun() : base(WorkerRunId.New())
    {
    }

    protected WorkerRun(WorkerRunId id, IssueId issueId, DateTimeOffset createdAt, WorkerRole role, string? image) : base(id)
    {
        IssueId = issueId;
        CreatedAt = createdAt;
        Role = role;
        Image = image;
    }

    public IssueId IssueId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>The role the run was dispatched with (ADR 0095).</summary>
    public WorkerRole Role { get; private set; } = WorkerRole.Implement;

    /// <summary>
    /// The worker image the run was dispatched on: the tag, pinned to the image ID when known
    /// (<c>tag@sha256:...</c>). <see langword="null"/> for runs recorded before images were.
    /// </summary>
    public string? Image { get; private set; }

    /// <summary>
    /// How the run's verdict (ADR 0094) was read. Recorded on the terminal run of a run that exited 0 and whose role
    /// has a verdict; <see langword="null"/> otherwise.
    /// </summary>
    public RunVerdictStatus? VerdictStatus { get; private set; }

    /// <summary>The questions of a <see cref="RunVerdictStatus.NeedsInput"/> verdict; <see langword="null"/> otherwise.</summary>
    public IReadOnlyList<RunVerdictQuestion>? VerdictQuestions { get; private set; }

    /// <summary>The run's recorded verdict, or <see langword="null"/> when none was recorded.</summary>
    public RunVerdict? Verdict => VerdictStatus is { } status ? RunVerdict.FromStored(status, VerdictQuestions) : null;

    /// <summary>The default branch's head the run started from (ADR 0100); <see langword="null"/> before it was prepared.</summary>
    public CommitSha? BaseDefaultBranchHead { get; private set; }

    /// <summary>The run's existing branch head it started from; <see langword="null"/> for a run with none.</summary>
    public CommitSha? BaseRunBranchHead { get; private set; }

    /// <summary>
    /// The run's base as the Publisher's Prepare mode reported it, or <see langword="null"/> for a run that never
    /// reached Prepare (or was recorded before runs had a base).
    /// </summary>
    public RunBase? Base => BaseDefaultBranchHead is { } defaultBranchHead ? new RunBase(defaultBranchHead, BaseRunBranchHead) : null;

    /// <summary>The branch head Foundry pushed when it published the run (ADR 0100); <see langword="null"/> when it pushed nothing.</summary>
    public CommitSha? PublishedHead { get; private set; }

    /// <summary>The commits the run's branch held beyond the default branch once the run was published.</summary>
    public int? PublishedBranchCommitCount { get; private set; }

    /// <summary>The side branch the run's work went to because its own branch diverged; <see langword="null"/> otherwise.</summary>
    public BranchName? DivergedSideBranch { get; private set; }

    protected void RecordPublishFacts(CommitSha? pushedHead, int? branchCommitCount, BranchName? divergedSideBranch)
    {
        PublishedHead = pushedHead;
        PublishedBranchCommitCount = branchCommitCount;
        DivergedSideBranch = divergedSideBranch;
    }

    protected void RecordBase(RunBase? runBase)
    {
        BaseDefaultBranchHead = runBase?.DefaultBranchHead;
        BaseRunBranchHead = runBase?.RunBranchHead;
    }

    protected void RecordVerdict(RunVerdict? verdict)
    {
        VerdictStatus = verdict?.Status;
        VerdictQuestions = verdict is { Questions.Count: > 0 } ? verdict.Questions : null;
    }
}
