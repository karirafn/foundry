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

    private static readonly IReadOnlyList<(string State, IssueStage Stage, bool NeedsAction, bool AwaitsDispatch, bool WaitsOnBlockers)> Expected =
    [
        ("detected", IssueStage.Triage, false, true, false),
        ("triage_queued", IssueStage.Triage, false, true, false),
        ("triage_blocked", IssueStage.Triage, false, true, true),
        ("triage_in_progress", IssueStage.Triage, false, false, false),
        ("triage_failed", IssueStage.Triage, true, false, false),
        ("queued", IssueStage.Implement, false, true, false),
        ("blocked", IssueStage.Implement, false, true, true),
        ("revision_queued", IssueStage.Implement, false, true, false),
        ("continuation_queued", IssueStage.Implement, false, true, false),
        ("revision_blocked", IssueStage.Implement, false, true, true),
        ("continuation_blocked", IssueStage.Implement, false, true, true),
        ("in_progress", IssueStage.Implement, false, false, false),
        ("revision_in_progress", IssueStage.Implement, false, false, false),
        ("rebase_pending", IssueStage.Implement, false, false, false),
        ("checks_pending", IssueStage.Implement, false, false, false),
        ("merge_pending", IssueStage.Merge, false, false, false),
        ("failed", IssueStage.Implement, true, false, false),
        ("continuable_failed", IssueStage.Implement, true, false, false),
        ("revision_failed", IssueStage.Implement, true, false, false),
        ("unchanged", IssueStage.Implement, true, false, false),
        ("publish_failed", IssueStage.Implement, true, false, false),
        ("publish_blocked", IssueStage.Implement, true, false, false),
        ("branch_diverged", IssueStage.Implement, true, false, false),
        ("review_queued", IssueStage.Review, false, true, false),
        ("review_in_progress", IssueStage.Review, false, false, false),
        ("review_failed", IssueStage.Review, false, false, false),
        ("awaiting_input (retriage)", IssueStage.Triage, true, false, false),
        ("awaiting_input (revision)", IssueStage.Implement, true, false, false),
        ("awaiting_input (continuation)", IssueStage.Implement, true, false, false),
        ("awaiting_input (fresh)", IssueStage.Implement, true, false, false),
        ("review", IssueStage.Merge, true, false, false),
        ("completed", IssueStage.Done, false, false, false),
    ];

    public static TheoryData<string, IssueStage> Stages => new(Expected.Select(e => (e.State, e.Stage)));

    public static TheoryData<string, bool> NeedsActions => new(Expected.Select(e => (e.State, e.NeedsAction)));

    public static TheoryData<string, bool> AwaitsDispatches => new(Expected.Select(e => (e.State, e.AwaitsDispatch)));

    public static TheoryData<string, bool> WaitsOnBlockersByState => new(Expected.Select(e => (e.State, e.WaitsOnBlockers)));

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
