using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
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

    /// <summary>
    /// The repository the Publisher prepared the run's base from; <see langword="null"/> for a run that never reached a
    /// successful Prepare (or was recorded before this was kept). A host-pause probe tries the most recently prepared
    /// repositories on a host first.
    /// </summary>
    public MonitoredRepositoryId? PreparedRepositoryId { get; private set; }

    /// <summary>The branch head Foundry pushed when it published the run (ADR 0100); <see langword="null"/> when it pushed nothing.</summary>
    public CommitSha? PublishedHead { get; private set; }

    /// <summary>The commits the run's branch held beyond the default branch once the run was published.</summary>
    public int? PublishedBranchCommitCount { get; private set; }

    /// <summary>The side branch the run's work went to because its own branch diverged; <see langword="null"/> otherwise.</summary>
    public BranchName? DivergedSideBranch { get; private set; }

    /// <summary>
    /// Why the run's work went to <see cref="DivergedSideBranch"/>; <see langword="null"/> when it did not diverge, or for a run
    /// recorded before the reason was kept (read as <see cref="BranchDivergenceReason.BranchMoved"/>).
    /// </summary>
    public BranchDivergenceReason? DivergenceReason { get; private set; }

    /// <summary>The side branch the run's work went to and why; <see langword="null"/> when it went to its own branch.</summary>
    public BranchDivergence? Divergence =>
        DivergedSideBranch is { } sideBranch
            ? new BranchDivergence(sideBranch, DivergenceReason ?? BranchDivergenceReason.BranchMoved)
            : null;

    /// <summary>The files the Diff gate reverted before it pushed the run (#788); empty when it reverted none.</summary>
    public IReadOnlyList<RevertedFile> RevertedFiles { get; private set; } = [];

    /// <summary>
    /// The secret findings an operator confirmed as false positives (#899), so the Publisher suppresses exactly these;
    /// empty when the run was never confirmed. Each confirmation adds to the set, so a re-block never un-suppresses an
    /// earlier finding.
    /// </summary>
    public IReadOnlyList<SecretFinding> ConfirmedFindings { get; private set; } = [];

    /// <summary>When an operator last confirmed the run's findings as false positives; <see langword="null"/> when never.</summary>
    public DateTimeOffset? FalsePositivesConfirmedAt { get; private set; }

    /// <summary>The run's confirmation, or <see langword="null"/> when no finding was ever confirmed.</summary>
    public FalsePositiveConfirmation? FalsePositiveConfirmation =>
        FalsePositivesConfirmedAt is { } confirmedAt && ConfirmedFindings.Count > 0
            ? new FalsePositiveConfirmation(ConfirmedFindings, confirmedAt)
            : null;

    /// <summary>
    /// The numbers of the open issues Foundry supplied in the run's Tracker Context at dispatch, before prompt fitting;
    /// <see langword="null"/> for a run that recorded none.
    /// </summary>
    public IReadOnlyList<int>? SuppliedOpenIssueNumbers { get; private set; }

    protected void StoreSuppliedOpenIssueNumbers(IReadOnlyList<int>? suppliedOpenIssueNumbers)
    {
        SuppliedOpenIssueNumbers = suppliedOpenIssueNumbers;
    }

    protected void RecordPublishFacts(
        CommitSha? pushedHead,
        int? branchCommitCount,
        BranchDivergence? divergence,
        IReadOnlyList<RevertedFile> revertedFiles)
    {
        ArgumentNullException.ThrowIfNull(revertedFiles);

        PublishedHead = pushedHead;
        PublishedBranchCommitCount = branchCommitCount;
        DivergedSideBranch = divergence?.SideBranch;
        DivergenceReason = divergence?.Reason;
        RevertedFiles = revertedFiles;
    }

    /// <summary>Adds <paramref name="findings"/> to the confirmed set, keeping each finding once.</summary>
    protected void ConfirmFindings(IReadOnlyList<SecretFinding> findings, DateTimeOffset confirmedAt)
    {
        ArgumentNullException.ThrowIfNull(findings);
        if (findings.Count == 0)
        {
            throw new ArgumentException("A confirmation confirms at least one finding.", nameof(findings));
        }

        ConfirmedFindings = [.. ConfirmedFindings.Concat(findings).Distinct()];
        FalsePositivesConfirmedAt = confirmedAt;
    }

    protected void CarryConfirmationFrom(WorkerRun source)
    {
        ArgumentNullException.ThrowIfNull(source);

        ConfirmedFindings = source.ConfirmedFindings;
        FalsePositivesConfirmedAt = source.FalsePositivesConfirmedAt;
    }

    protected void RecordBase(RunBase? runBase, MonitoredRepositoryId? preparedRepositoryId)
    {
        BaseDefaultBranchHead = runBase?.DefaultBranchHead;
        BaseRunBranchHead = runBase?.RunBranchHead;
        PreparedRepositoryId = preparedRepositoryId;
    }

    protected void CarryDispatchFactsFrom(WorkerRun source)
    {
        ArgumentNullException.ThrowIfNull(source);

        RecordBase(source.Base, source.PreparedRepositoryId);
        StoreSuppliedOpenIssueNumbers(source.SuppliedOpenIssueNumbers);
    }

    protected void RecordVerdict(RunVerdict? verdict)
    {
        VerdictStatus = verdict?.Status;
        VerdictQuestions = verdict is { Questions.Count: > 0 } ? verdict.Questions : null;
    }
}
