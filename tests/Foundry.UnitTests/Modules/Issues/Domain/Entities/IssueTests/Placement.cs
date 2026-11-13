using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.IssueTests;

/// <summary>
/// Every state type declares where it sits in the lifecycle: its stage, whether it needs a person, whether it waits for a
/// worker run to be dispatched (#851), whether it waits for its open blockers to close (#892), and the code step Foundry
/// runs for it (#851).
/// </summary>
public sealed class Placement
{
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
        ["in_progress"] = () => new IssueBuilder().InProgress(),
        ["revision_in_progress"] = () => new IssueBuilder().RevisionInProgress(),
        ["rebase_pending"] = () => new IssueBuilder().RebasePending(),
        ["checks_pending"] = () => new IssueBuilder().ChecksPending(),
        ["failed"] = () => new IssueBuilder().Failed(),
        ["continuable_failed"] = () => new IssueBuilder().ContinuableFailed(),
        ["revision_failed"] = () => new IssueBuilder().RevisionFailed(),
        ["unchanged"] = () => new IssueBuilder().Unchanged(),
        ["publish_failed"] = () => new IssueBuilder().PublishFailed(),
        ["publish_blocked"] = () => new IssueBuilder().PublishBlocked(),
        ["branch_diverged"] = () => new IssueBuilder().BranchDiverged(),
        ["awaiting_input (retriage)"] = () => new IssueBuilder().AwaitingInputFromTriage(IssueKind.Feature),
        ["awaiting_input (revision)"] = () => new IssueBuilder().AwaitingInput(),
        ["awaiting_input (continuation)"] = () => new IssueBuilder().AwaitingInputWithoutPullRequest(branchHasCommits: true),
        ["awaiting_input (fresh)"] = () => new IssueBuilder().AwaitingInputWithoutPullRequest(branchHasCommits: false),
        ["review"] = () => new IssueBuilder().Review(),
        ["completed"] = () => new IssueBuilder().Completed(),
    };

    private static readonly IReadOnlyList<(string State, IssueStage Stage, bool NeedsAction, bool AwaitsDispatch, bool WaitsOnBlockers, IssueCodeStep? CodeStep)> Expected =
    [
        ("detected", IssueStage.Triage, false, true, false, null),
        ("triage_queued", IssueStage.Triage, false, true, false, null),
        ("triage_blocked", IssueStage.Triage, false, true, true, null),
        ("triage_in_progress", IssueStage.Triage, false, false, false, null),
        ("triage_failed", IssueStage.Triage, true, false, false, null),
        ("queued", IssueStage.Implement, false, true, false, null),
        ("blocked", IssueStage.Implement, false, true, true, null),
        ("revision_queued", IssueStage.Implement, false, true, false, null),
        ("continuation_queued", IssueStage.Implement, false, true, false, null),
        ("in_progress", IssueStage.Implement, false, false, false, null),
        ("revision_in_progress", IssueStage.Implement, false, false, false, null),
        ("rebase_pending", IssueStage.Implement, false, false, false, IssueCodeStep.Rebasing),
        ("checks_pending", IssueStage.Implement, false, false, false, IssueCodeStep.Checks),
        ("failed", IssueStage.Implement, true, false, false, null),
        ("continuable_failed", IssueStage.Implement, true, false, false, null),
        ("revision_failed", IssueStage.Implement, true, false, false, null),
        ("unchanged", IssueStage.Implement, true, false, false, null),
        ("publish_failed", IssueStage.Implement, true, false, false, null),
        ("publish_blocked", IssueStage.Implement, true, false, false, null),
        ("branch_diverged", IssueStage.Implement, true, false, false, null),
        ("awaiting_input (retriage)", IssueStage.Triage, true, false, false, null),
        ("awaiting_input (revision)", IssueStage.Implement, true, false, false, null),
        ("awaiting_input (continuation)", IssueStage.Implement, true, false, false, null),
        ("awaiting_input (fresh)", IssueStage.Implement, true, false, false, null),
        ("review", IssueStage.AwaitingReview, true, false, false, null),
        ("completed", IssueStage.Done, false, false, false, null),
    ];

    public static TheoryData<string, IssueStage> Stages => new(Expected.Select(e => (e.State, e.Stage)));

    public static TheoryData<string, bool> NeedsActions => new(Expected.Select(e => (e.State, e.NeedsAction)));

    public static TheoryData<string, bool> AwaitsDispatches => new(Expected.Select(e => (e.State, e.AwaitsDispatch)));

    public static TheoryData<string, bool> WaitsOnBlockersByState => new(Expected.Select(e => (e.State, e.WaitsOnBlockers)));

    public static TheoryData<string, IssueCodeStep?> CodeSteps => new(Expected.Select(e => (e.State, e.CodeStep)));

    [Theory]
    [MemberData(nameof(Stages))]
    public void WhenInState_DeclaresItsStage(string state, IssueStage stage)
    {
        // Arrange
        Issue issue = States[state]();

        // Act
        IssueStage actual = issue.Stage;

        // Assert
        actual.ShouldBe(stage);
    }

    [Theory]
    [MemberData(nameof(NeedsActions))]
    public void WhenInState_DeclaresWhetherItNeedsAction(string state, bool needsAction)
    {
        // Arrange
        Issue issue = States[state]();

        // Act
        bool actual = issue.NeedsAction;

        // Assert
        actual.ShouldBe(needsAction);
    }

    [Theory]
    [MemberData(nameof(AwaitsDispatches))]
    public void WhenInState_DeclaresWhetherItAwaitsDispatch(string state, bool awaitsDispatch)
    {
        // Arrange
        Issue issue = States[state]();

        // Act
        bool actual = issue.AwaitsDispatch;

        // Assert
        actual.ShouldBe(awaitsDispatch);
    }

    [Theory]
    [MemberData(nameof(WaitsOnBlockersByState))]
    public void WhenInState_DeclaresWhetherItWaitsOnBlockers(string state, bool waitsOnBlockers)
    {
        // Arrange
        Issue issue = States[state]();

        // Act
        bool actual = issue.WaitsOnBlockers;

        // Assert
        actual.ShouldBe(waitsOnBlockers);
    }

    [Theory]
    [MemberData(nameof(CodeSteps))]
    public void WhenInState_DeclaresItsCodeStep(string state, IssueCodeStep? codeStep)
    {
        // Arrange
        Issue issue = States[state]();

        // Act
        IssueCodeStep? actual = issue.CodeStep;

        // Assert
        actual.ShouldBe(codeStep);
    }

    [Fact]
    public void WhenEveryStateTypeIsListed_EachHasAnExpectedPlacement()
    {
        // Arrange
        IReadOnlyList<Type> stateTypes = typeof(Issue).Assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(Issue)) && !t.IsAbstract)
            .ToList();

        // Act
        IReadOnlyList<Type> covered = States.Values.Select(build => build().GetType()).Distinct().ToList();

        // Assert
        stateTypes.Except(covered).ShouldBeEmpty();
    }
}
