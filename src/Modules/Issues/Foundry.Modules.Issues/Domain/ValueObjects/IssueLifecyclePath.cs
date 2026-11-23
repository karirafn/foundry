using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Workers.Contracts;

namespace Foundry.Modules.Issues.Domain.ValueObjects;

/// <summary>One step on an issue's lifecycle path; <paramref name="Phase"/> is set only on the current step.</summary>
public sealed record LifecyclePathStep(
    IssueLifecycleStep Step,
    LifecycleActor Actor,
    LifecycleStepStatus Status,
    LifecyclePhase? Phase);

/// <summary>
/// Builds an issue's whole lifecycle path: every step in order, who runs it, its status and, on the current step, its
/// phase. The repository settings and <see cref="Issue.HasPersonFeedback"/> decide which steps are off or skipped.
/// </summary>
public static class IssueLifecyclePath
{
    private static readonly IssueLifecycleStep[] Steps = Enum.GetValues<IssueLifecycleStep>();

    /// <param name="runPhase">The phase of the issue's worker run, or <see langword="null"/> when it reports none.</param>
    /// <param name="waitsToCatchUp">True when the issue's catch-up queue entry waits for its turn (ADR 0111).</param>
    public static IReadOnlyList<LifecyclePathStep> For(
        Issue issue,
        WorkerRunPhase? runPhase,
        bool waitsToCatchUp,
        RepositoryLifecycleSettings settings)
    {
        ArgumentNullException.ThrowIfNull(issue);

        return Build(
            CurrentStep(issue, runPhase),
            CurrentPhase(issue, runPhase, waitsToCatchUp),
            issue.HasPersonFeedback,
            settings);
    }

    public static IReadOnlyList<LifecyclePathStep> Build(
        IssueLifecycleStep current,
        LifecyclePhase? phase,
        bool hasPersonFeedback,
        RepositoryLifecycleSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return Steps
            .Select(step =>
            {
                LifecycleStepStatus status = StatusOf(step, current, hasPersonFeedback, settings);
                return new LifecyclePathStep(
                    step,
                    ActorOf(step),
                    status,
                    status == LifecycleStepStatus.Current ? phase : null);
            })
            .ToList();
    }

    // Publishing is a phase of the run rather than of the issue, so a working issue whose run publishes sits on Publish.
    private static IssueLifecycleStep CurrentStep(Issue issue, WorkerRunPhase? runPhase) =>
        issue is InProgressIssue or RevisionInProgressIssue
            && runPhase is WorkerRunPhase.Publishing or WorkerRunPhase.Retrying
            ? IssueLifecycleStep.Publish
            : issue.LifecycleStep;

    // A completed issue's Done step has nothing left to do, so it carries no phase.
    private static LifecyclePhase? CurrentPhase(Issue issue, WorkerRunPhase? runPhase, bool waitsToCatchUp) =>
        issue switch
        {
            CompletedIssue => null,
            InProgressIssue or RevisionInProgressIssue or TriageInProgressIssue or ReviewInProgressIssue
                when runPhase is WorkerRunPhase.Starting => LifecyclePhase.Preparing,
            InProgressIssue or RevisionInProgressIssue
                when runPhase is WorkerRunPhase.Retrying => LifecyclePhase.Retrying,
            ReviewFailedIssue => LifecyclePhase.Retrying,
            FailedIssue
                or ContinuableFailedIssue
                or RevisionFailedIssue
                or TriageFailedIssue
                or PublishFailedIssue
                or PublishBlockedIssue => LifecyclePhase.Failed,
            AwaitingInputIssue or UnchangedIssue or ReviewIssue or BranchDivergedIssue => LifecyclePhase.NeedsYou,
            MergePendingIssue when waitsToCatchUp => LifecyclePhase.Waiting,
            _ when IsWait(issue.LifecycleStep) => LifecyclePhase.Waiting,
            _ => LifecyclePhase.Running,
        };

    private static bool IsWait(IssueLifecycleStep step) =>
        step is IssueLifecycleStep.Detected
            or IssueLifecycleStep.TriageQueued
            or IssueLifecycleStep.Queued
            or IssueLifecycleStep.ReviewQueued;

    // The current step wins over off and skipped: a setting switched off while its step runs still shows it running.
    private static LifecycleStepStatus StatusOf(
        IssueLifecycleStep step,
        IssueLifecycleStep current,
        bool hasPersonFeedback,
        RepositoryLifecycleSettings settings)
    {
        if (step == current)
        {
            return LifecycleStepStatus.Current;
        }

        if (IsOff(step, settings))
        {
            return LifecycleStepStatus.Off;
        }

        if (IsSkipped(step, current, hasPersonFeedback, settings))
        {
            return LifecycleStepStatus.Skipped;
        }

        return step < current ? LifecycleStepStatus.Done : LifecycleStepStatus.Upcoming;
    }

    private static bool IsOff(IssueLifecycleStep step, RepositoryLifecycleSettings settings) =>
        step switch
        {
            IssueLifecycleStep.Rebase => !settings.RebaseAfterRun,
            IssueLifecycleStep.ReviewQueued or IssueLifecycleStep.Review => !settings.ReviewPullRequests,
            IssueLifecycleStep.Merge => !settings.MergeWhenChecksPass,
            _ => false,
        };

    // A person's feedback routes a passed verdict to Your review instead of the merge (DOMAIN.md § Has Person
    // Feedback), and an issue in Your review did not merge; with neither, a merge-on repository merges without one.
    private static bool IsSkipped(
        IssueLifecycleStep step,
        IssueLifecycleStep current,
        bool hasPersonFeedback,
        RepositoryLifecycleSettings settings) =>
        step switch
        {
            IssueLifecycleStep.Merge => hasPersonFeedback || current == IssueLifecycleStep.YourReview,
            IssueLifecycleStep.YourReview => settings.MergeWhenChecksPass && !hasPersonFeedback,
            _ => false,
        };

    private static LifecycleActor ActorOf(IssueLifecycleStep step) =>
        step switch
        {
            IssueLifecycleStep.Triage or IssueLifecycleStep.Implement or IssueLifecycleStep.Review => LifecycleActor.Claude,
            IssueLifecycleStep.YourReview => LifecycleActor.Person,
            _ => LifecycleActor.Code,
        };
}
