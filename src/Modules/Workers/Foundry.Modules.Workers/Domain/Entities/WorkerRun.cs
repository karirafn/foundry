using System.Linq.Expressions;

using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Shared;

namespace Foundry.Modules.Workers.Domain.Entities;

public abstract class WorkerRun : AggregateRoot<WorkerRunId>, IStateMachine<WorkerRun>
{
    /// <summary>
    /// Whether a run holds a Worker Slot. A slot bounds concurrent Claude token use, so only a run that is starting or
    /// running <c>claude</c> holds one; once its container stops, publishing and any other <see cref="UnpublishedRun"/>
    /// state hold none.
    /// </summary>
    public static readonly Expression<Func<WorkerRun, bool>> HoldsSlot = r => r is StartingRun || r is ActiveRun;

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
    public IReadOnlyList<InputQuestion>? VerdictQuestions { get; private set; }

    /// <summary>The run's recorded verdict, or <see langword="null"/> when none was recorded.</summary>
    public RunVerdict? Verdict => VerdictStatus is { } status ? RunVerdict.FromStored(status, VerdictQuestions) : null;

    /// <summary>
    /// Whether the run was started by Continue without answer (ADR 0096): it may not send its issue straight back to
    /// awaiting input, so a <c>needs_input</c> verdict is refused and the fact-based outcome applies.
    /// </summary>
    public bool RefusesNeedsInput { get; private set; }

    /// <summary>
    /// Whether the run returned a <c>needs_input</c> verdict that was refused because the run was started by Continue
    /// without answer: the refusal as recorded on the run.
    /// </summary>
    public bool NeedsInputRefused => RefusesNeedsInput && VerdictStatus == RunVerdictStatus.NeedsInput;

    /// <summary>Whether the run asks for input: a <c>needs_input</c> verdict that was not refused.</summary>
    public bool AsksForInput => VerdictStatus == RunVerdictStatus.NeedsInput && !RefusesNeedsInput;

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

    /// <summary>
    /// The hash of the pull request body Foundry last wrote for this run (ADR 0100); <see langword="null"/> when it
    /// wrote none. A later run of the issue replaces the body only while the live body still hashes to it.
    /// </summary>
    public PullRequestBodyHash? PullRequestBodyHash { get; private set; }

    protected void StorePullRequestBodyHash(PullRequestBodyHash? pullRequestBodyHash)
    {
        PullRequestBodyHash = pullRequestBodyHash;
    }

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
    /// The Diff gate's generic findings (#956, ADR 0107): matches of a generic pattern that the Publisher pushed with the
    /// run for review in its pull request; empty when it found none. They never block and are never confirmed.
    /// </summary>
    public IReadOnlyList<SecretFinding> GenericFindings { get; private set; } = [];

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

    /// <summary>
    /// The facts code computed for a review run at dispatch (ADR 0114), against which its verdict is checked;
    /// <see langword="null"/> for any other run.
    /// </summary>
    public SuppliedReviewFacts? SuppliedReviewFacts { get; private set; }

    /// <summary>
    /// The valid verdict a review run returned, with the outcome code derived from it (ADR 0113); <see langword="null"/>
    /// for any other run and for a review that ended without a valid verdict.
    /// </summary>
    public RecordedReviewVerdict? ReviewVerdict { get; private set; }

    /// <summary>
    /// The fingerprint of the spec the issue body Foundry supplied at dispatch states; a triage run's completion records
    /// it on the issue as the spec it judged. <see langword="null"/> for a run that recorded none.
    /// </summary>
    public SpecHash? SuppliedSpecHash { get; private set; }

    protected void StoreSuppliedSpecHash(SpecHash? suppliedSpecHash)
    {
        SuppliedSpecHash = suppliedSpecHash;
    }

    protected void StoreSuppliedReviewFacts(SuppliedReviewFacts? suppliedReviewFacts)
    {
        SuppliedReviewFacts = suppliedReviewFacts;
    }

    protected void RecordReviewVerdict(RecordedReviewVerdict? reviewVerdict)
    {
        ReviewVerdict = reviewVerdict;
    }

    /// <summary>
    /// The hash of the run's Run Source Token (ADR 0112), minted when the run begins and kept through every state;
    /// <see langword="null"/> for a run recorded before runs had one.
    /// </summary>
    public RunSourceTokenHash? SourceTokenHash { get; private set; }

    protected void StoreSourceTokenHash(RunSourceTokenHash sourceTokenHash)
    {
        SourceTokenHash = sourceTokenHash;
    }

    /// <summary>
    /// The toolchain the run's container reported when it started (ADR 0109). <see langword="null"/> for a run whose
    /// container reported none (bootstrap failed first, or the line was missing or malformed) and for runs recorded
    /// before runs kept it.
    /// </summary>
    public Toolchain? Toolchain { get; private set; }

    /// <summary>
    /// The Implement Demand an implement run was dispatched with, the effective demand at claim time;
    /// <see langword="null"/> for a triage or review run and for runs recorded before runs kept it.
    /// </summary>
    public ImplementDemand? Demand { get; private set; }

    /// <summary>
    /// The model written to the run's managed settings, empty when none was written so the account default applied;
    /// <see langword="null"/> for runs recorded before runs kept it.
    /// </summary>
    public string? Model { get; private set; }

    /// <summary>
    /// The effort written to the run's managed settings, empty when none was (always for triage and review);
    /// <see langword="null"/> for runs recorded before runs kept it.
    /// </summary>
    public string? Effort { get; private set; }

    /// <summary>
    /// The plan usage the first <c>rate_limit_event</c> in the run's output reported; <see langword="null"/> when none
    /// reported it (an API-key account, an older CLI, a run that ended before its first event, or a run whose full
    /// log could not be read).
    /// </summary>
    public PlanUsage? PlanUsageAtStart { get; private set; }

    /// <summary>The plan usage the last <c>rate_limit_event</c> in the run's output reported; null exactly when <see cref="PlanUsageAtStart"/> is.</summary>
    public PlanUsage? PlanUsageAtEnd { get; private set; }

    protected void StoreRunModel(RunModel runModel)
    {
        ArgumentNullException.ThrowIfNull(runModel);
        if (runModel.Demand is not null && Role != WorkerRole.Implement)
        {
            throw new ArgumentException($"Only an implement run has a demand; this run's role is '{Role.Name}'.", nameof(runModel));
        }

        Demand = runModel.Demand;
        Model = runModel.Model;
        Effort = runModel.Effort;
    }

    protected void StorePlanUsage(RunPlanUsage? planUsage)
    {
        PlanUsageAtStart = planUsage?.AtStart;
        PlanUsageAtEnd = planUsage?.AtEnd;
    }

    protected void StoreSuppliedOpenIssueNumbers(IReadOnlyList<int>? suppliedOpenIssueNumbers)
    {
        SuppliedOpenIssueNumbers = suppliedOpenIssueNumbers;
    }

    protected void StoreToolchain(Toolchain? toolchain)
    {
        Toolchain = toolchain;
    }

    protected void RefuseNeedsInput()
    {
        RefusesNeedsInput = true;
    }

    protected void RecordPublishFacts(
        CommitSha? pushedHead,
        int? branchCommitCount,
        BranchDivergence? divergence,
        IReadOnlyList<RevertedFile> revertedFiles,
        IReadOnlyList<SecretFinding> genericFindings)
    {
        ArgumentNullException.ThrowIfNull(revertedFiles);
        ArgumentNullException.ThrowIfNull(genericFindings);

        PublishedHead = pushedHead;
        PublishedBranchCommitCount = branchCommitCount;
        DivergedSideBranch = divergence?.SideBranch;
        DivergenceReason = divergence?.Reason;
        RevertedFiles = revertedFiles;
        GenericFindings = genericFindings;
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
        StoreSuppliedReviewFacts(source.SuppliedReviewFacts);
        StoreSuppliedSpecHash(source.SuppliedSpecHash);
        SourceTokenHash = source.SourceTokenHash;
        StoreToolchain(source.Toolchain);
        Demand = source.Demand;
        Model = source.Model;
        Effort = source.Effort;
        PlanUsageAtStart = source.PlanUsageAtStart;
        PlanUsageAtEnd = source.PlanUsageAtEnd;
        RefusesNeedsInput = source.RefusesNeedsInput;
    }

    protected void RecordVerdict(RunVerdict? verdict)
    {
        VerdictStatus = verdict?.Status;
        VerdictQuestions = verdict is { Questions.Count: > 0 } ? verdict.Questions : null;
    }
}
