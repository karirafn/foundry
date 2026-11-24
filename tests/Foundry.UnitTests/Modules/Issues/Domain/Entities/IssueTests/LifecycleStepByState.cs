using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Workers.Contracts;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.IssueTests;

/// <summary>
/// Every state type declares its place on the lifecycle path, and the path gives that step its actor and phase. A state
/// type with no row here fails <see cref="WhenEveryStateTypeIsListed_EachHasAnExpectedLifecycleStep"/>.
/// </summary>
public sealed class LifecycleStepByState
{
    private static readonly RepositoryLifecycleSettings AllOn = new(
        RebaseAfterRun: true,
        ReviewPullRequests: true,
        MergeWhenChecksPass: true);

    private static readonly IReadOnlyDictionary<string, Func<Issue>> States = new Dictionary<string, Func<Issue>>
    {
        ["detected"] = () => new IssueBuilder().Detected(),
        ["triage_queued"] = () => new IssueBuilder().TriageQueued(),
        ["triage_blocked"] = () => new IssueBuilder().TriageBlocked([2]),
        ["triage_in_progress"] = () => new IssueBuilder().TriageInProgress(),
        ["triage_failed"] = () => new IssueBuilder().TriageFailed(),
        ["queued"] = () => new IssueBuilder().FreshQueued(),
        ["blocked"] = () => new IssueBuilder().WithBlockedBy([2]).TriageFailed().SkipTriage(DateTimeOffset.UtcNow),
        ["revision_queued"] = () => new IssueBuilder().RevisionQueued(),
        ["continuation_queued"] = () => new IssueBuilder().ContinuableFailed().Retry(DateTimeOffset.UtcNow),
        ["revision_blocked"] = () => new IssueBuilder().RevisionBlocked([2]),
        ["continuation_blocked"] = () => new IssueBuilder().ContinuationBlocked([2]),
        ["in_progress"] = () => new IssueBuilder().InProgress(),
        ["revision_in_progress"] = () => new IssueBuilder().RevisionInProgress(),
        ["rebase_pending"] = () => new IssueBuilder().RebasePending(),
        ["checks_pending"] = () => new IssueBuilder().ChecksPending(),
        ["merge_pending"] = () => new IssueBuilder().MergePending(),
        ["failed"] = () => new IssueBuilder().Failed(),
        ["continuable_failed"] = () => new IssueBuilder().ContinuableFailed(),
        ["revision_failed"] = () => new IssueBuilder().RevisionFailed(),
        ["unchanged"] = () => new IssueBuilder().Unchanged(),
        ["publish_failed"] = () => new IssueBuilder().PublishFailed(),
        ["publish_blocked"] = () => new IssueBuilder().PublishBlocked(),
        ["branch_diverged"] = () => new IssueBuilder().BranchDiverged(),
        ["review_queued"] = () => new IssueBuilder().ReviewQueued(),
        ["review_in_progress"] = () => new IssueBuilder().ReviewInProgress(),
        ["review_failed"] = () => new IssueBuilder().ReviewFailed(),
        ["awaiting_input (retriage)"] = () => new IssueBuilder().AwaitingInputFromTriage(IssueKind.Feature),
        ["awaiting_input (revision)"] = () => new IssueBuilder().AwaitingInput(),
        ["awaiting_input (continuation)"] = () => new IssueBuilder().AwaitingInputWithoutPullRequest(branchHasCommits: true),
        ["awaiting_input (fresh)"] = () => new IssueBuilder().AwaitingInputWithoutPullRequest(branchHasCommits: false),
        ["review"] = () => new IssueBuilder().Review(),
        ["completed"] = () => new IssueBuilder().Completed(),
    };

    // (state, run phase, waits to catch up) → (current step, actor, phase)
    private static readonly IReadOnlyList<(string State, WorkerRunPhase? RunPhase, bool WaitsToCatchUp, IssueLifecycleStep Step, LifecycleActor Actor, LifecyclePhase? Phase)> Rows =
    [
        ("detected", null, false, IssueLifecycleStep.Detected, LifecycleActor.Code, LifecyclePhase.Waiting),
        ("triage_queued", null, false, IssueLifecycleStep.TriageQueued, LifecycleActor.Code, LifecyclePhase.Waiting),
        ("triage_blocked", null, false, IssueLifecycleStep.TriageQueued, LifecycleActor.Code, LifecyclePhase.Waiting),
        ("triage_in_progress", null, false, IssueLifecycleStep.Triage, LifecycleActor.Claude, LifecyclePhase.Running),
        ("triage_in_progress", WorkerRunPhase.Starting, false, IssueLifecycleStep.Triage, LifecycleActor.Claude, LifecyclePhase.Preparing),
        ("triage_failed", null, false, IssueLifecycleStep.Triage, LifecycleActor.Claude, LifecyclePhase.Failed),
        ("queued", null, false, IssueLifecycleStep.Queued, LifecycleActor.Code, LifecyclePhase.Waiting),
        ("blocked", null, false, IssueLifecycleStep.Queued, LifecycleActor.Code, LifecyclePhase.Waiting),
        ("revision_queued", null, false, IssueLifecycleStep.Queued, LifecycleActor.Code, LifecyclePhase.Waiting),
        ("continuation_queued", null, false, IssueLifecycleStep.Queued, LifecycleActor.Code, LifecyclePhase.Waiting),
        ("revision_blocked", null, false, IssueLifecycleStep.Queued, LifecycleActor.Code, LifecyclePhase.Waiting),
        ("continuation_blocked", null, false, IssueLifecycleStep.Queued, LifecycleActor.Code, LifecyclePhase.Waiting),
        ("in_progress", null, false, IssueLifecycleStep.Implement, LifecycleActor.Claude, LifecyclePhase.Running),
        ("in_progress", WorkerRunPhase.Starting, false, IssueLifecycleStep.Implement, LifecycleActor.Claude, LifecyclePhase.Preparing),
        ("in_progress", WorkerRunPhase.Publishing, false, IssueLifecycleStep.Publish, LifecycleActor.Code, LifecyclePhase.Running),
        ("in_progress", WorkerRunPhase.Retrying, false, IssueLifecycleStep.Publish, LifecycleActor.Code, LifecyclePhase.Retrying),
        ("revision_in_progress", null, false, IssueLifecycleStep.Implement, LifecycleActor.Claude, LifecyclePhase.Running),
        ("revision_in_progress", WorkerRunPhase.Starting, false, IssueLifecycleStep.Implement, LifecycleActor.Claude, LifecyclePhase.Preparing),
        ("revision_in_progress", WorkerRunPhase.Publishing, false, IssueLifecycleStep.Publish, LifecycleActor.Code, LifecyclePhase.Running),
        ("revision_in_progress", WorkerRunPhase.Retrying, false, IssueLifecycleStep.Publish, LifecycleActor.Code, LifecyclePhase.Retrying),
        ("rebase_pending", null, false, IssueLifecycleStep.Rebase, LifecycleActor.Code, LifecyclePhase.Running),
        ("checks_pending", null, false, IssueLifecycleStep.Checks, LifecycleActor.Code, LifecyclePhase.Running),
        ("merge_pending", null, false, IssueLifecycleStep.Merge, LifecycleActor.Code, LifecyclePhase.Running),
        ("merge_pending", null, true, IssueLifecycleStep.Merge, LifecycleActor.Code, LifecyclePhase.Waiting),
        ("failed", null, false, IssueLifecycleStep.Implement, LifecycleActor.Claude, LifecyclePhase.Failed),
        ("continuable_failed", null, false, IssueLifecycleStep.Implement, LifecycleActor.Claude, LifecyclePhase.Failed),
        ("revision_failed", null, false, IssueLifecycleStep.Implement, LifecycleActor.Claude, LifecyclePhase.Failed),
        ("unchanged", null, false, IssueLifecycleStep.Implement, LifecycleActor.Claude, LifecyclePhase.NeedsYou),
        ("publish_failed", null, false, IssueLifecycleStep.Publish, LifecycleActor.Code, LifecyclePhase.Failed),
        ("publish_blocked", null, false, IssueLifecycleStep.Publish, LifecycleActor.Code, LifecyclePhase.Failed),
        ("branch_diverged", null, false, IssueLifecycleStep.Publish, LifecycleActor.Code, LifecyclePhase.NeedsYou),
        ("review_queued", null, false, IssueLifecycleStep.ReviewQueued, LifecycleActor.Code, LifecyclePhase.Waiting),
        ("review_in_progress", null, false, IssueLifecycleStep.Review, LifecycleActor.Claude, LifecyclePhase.Running),
        ("review_in_progress", WorkerRunPhase.Starting, false, IssueLifecycleStep.Review, LifecycleActor.Claude, LifecyclePhase.Preparing),
        ("review_failed", null, false, IssueLifecycleStep.Review, LifecycleActor.Claude, LifecyclePhase.Retrying),
        ("awaiting_input (retriage)", null, false, IssueLifecycleStep.Triage, LifecycleActor.Claude, LifecyclePhase.NeedsYou),
        ("awaiting_input (revision)", null, false, IssueLifecycleStep.Implement, LifecycleActor.Claude, LifecyclePhase.NeedsYou),
        ("awaiting_input (continuation)", null, false, IssueLifecycleStep.Implement, LifecycleActor.Claude, LifecyclePhase.NeedsYou),
        ("awaiting_input (fresh)", null, false, IssueLifecycleStep.Implement, LifecycleActor.Claude, LifecyclePhase.NeedsYou),
        ("review", null, false, IssueLifecycleStep.YourReview, LifecycleActor.Person, LifecyclePhase.NeedsYou),
        ("completed", null, false, IssueLifecycleStep.Done, LifecycleActor.Code, null),
    ];

    public static TheoryData<string, WorkerRunPhase?, bool, IssueLifecycleStep, LifecycleActor, LifecyclePhase?> Expected =>
        new(Rows.Select(r => (r.State, r.RunPhase, r.WaitsToCatchUp, r.Step, r.Actor, r.Phase)));

    [Theory]
    [MemberData(nameof(Expected))]
    public void WhenPathIsBuilt_TheCurrentStepHasTheStatesStepActorAndPhase(
        string state,
        WorkerRunPhase? runPhase,
        bool waitsToCatchUp,
        IssueLifecycleStep step,
        LifecycleActor actor,
        LifecyclePhase? phase)
    {
        // Arrange
        Issue issue = States[state]();

        // Act
        IReadOnlyList<LifecyclePathStep> path = IssueLifecyclePath.For(issue, runPhase, waitsToCatchUp, AllOn);

        // Assert
        LifecyclePathStep current = path.Single(s => s.Status == LifecycleStepStatus.Current);
        (current.Step, current.Actor, current.Phase).ShouldBe((step, actor, phase));
    }

    public static TheoryData<string, WorkerRunPhase?, bool> Inputs => new(
        Rows.Select(r => (r.State, r.RunPhase, r.WaitsToCatchUp)));

    [Theory]
    [MemberData(nameof(Inputs))]
    public void WhenPathIsBuilt_OnlyTheCurrentStepHasAPhase(string state, WorkerRunPhase? runPhase, bool waitsToCatchUp)
    {
        // Arrange
        Issue issue = States[state]();

        // Act
        IReadOnlyList<LifecyclePathStep> path = IssueLifecyclePath.For(issue, runPhase, waitsToCatchUp, AllOn);

        // Assert
        path.Where(s => s.Status != LifecycleStepStatus.Current).ShouldAllBe(s => s.Phase == null);
    }

    [Fact]
    public void WhenEveryStateTypeIsListed_EachHasAnExpectedLifecycleStep()
    {
        // Arrange
        IReadOnlyList<Type> stateTypes = typeof(Issue).Assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(Issue)) && !t.IsAbstract)
            .ToList();

        // Act
        IReadOnlyList<Type> covered = States.Keys
            .Where(state => Rows.Any(row => row.State == state))
            .Select(state => States[state]().GetType())
            .Distinct()
            .ToList();

        // Assert
        stateTypes.Except(covered).ShouldBeEmpty();
    }
}
