using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.IssueTests;

/// <summary>
/// Every state type declares the run kind it runs next (#1059): triage before triage, fresh or continuation for an
/// implement run, and a revision split by its trigger. In-flight states declare the run they are running (#1060), and
/// states with no run ahead declare none.
/// </summary>
public sealed class RunKindByState
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
        ["revision_queued (review feedback)"] = () => new IssueBuilder().RevisionQueued(),
        ["revision_queued (failed checks)"] = () => new IssueBuilder().ChecksPending()
            .FixChecks([new FailingCheck("ci/build", "failure", null)], DateTimeOffset.UtcNow),
        ["revision_queued (rebase conflict)"] = () => new IssueBuilder().RebasePending().ResolveConflict(DateTimeOffset.UtcNow),
        ["revision_queued (review findings)"] = () => new IssueBuilder().RevisionQueuedFromReviewFindings(),
        ["revision_queued (input provided)"] = () => (RevisionQueuedIssue)new IssueBuilder()
            .AwaitingInputPosted(DateTimeOffset.UtcNow.AddMinutes(-5))
            .ProvideInput(ClarificationInput.FromComments("Use SQLite."), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
        ["continuation_queued"] = () => new IssueBuilder().ContinuableFailed().Retry(DateTimeOffset.UtcNow),
        ["revision_blocked"] = () => new IssueBuilder().RevisionBlocked([2]),
        ["continuation_blocked"] = () => new IssueBuilder().ContinuationBlocked([2]),
        ["in_progress (fresh)"] = () => new IssueBuilder().InProgress(),
        ["in_progress (continuation)"] = () => new IssueBuilder().ContinuableFailed().Retry(DateTimeOffset.UtcNow)
            .Claim(WorkerRunId.New(), DateTimeOffset.UtcNow),
        ["revision_in_progress (review feedback)"] = () => new IssueBuilder().RevisionInProgress(),
        ["revision_in_progress (failed checks)"] = () => new IssueBuilder().ChecksPending()
            .FixChecks([new FailingCheck("ci/build", "failure", null)], DateTimeOffset.UtcNow)
            .Claim(WorkerRunId.New(), DateTimeOffset.UtcNow),
        ["revision_in_progress (rebase conflict)"] = () => new IssueBuilder().RebasePending()
            .ResolveConflict(DateTimeOffset.UtcNow).Claim(WorkerRunId.New(), DateTimeOffset.UtcNow),
        ["revision_in_progress (review findings)"] = () => new IssueBuilder().RevisionQueuedFromReviewFindings()
            .Claim(WorkerRunId.New(), DateTimeOffset.UtcNow),
        ["revision_in_progress (input provided)"] = () => ((RevisionQueuedIssue)new IssueBuilder()
            .AwaitingInputPosted(DateTimeOffset.UtcNow.AddMinutes(-5))
            .ProvideInput(ClarificationInput.FromComments("Use SQLite."), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow))
            .Claim(WorkerRunId.New(), DateTimeOffset.UtcNow),
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
        ["awaiting_input"] = () => new IssueBuilder().AwaitingInput(),
        ["review"] = () => new IssueBuilder().Review(),
        ["completed"] = () => new IssueBuilder().Completed(),
    };

    private static readonly IReadOnlyList<(string State, RunKind? RunKind)> Expected =
    [
        ("detected", RunKind.Triage),
        ("triage_queued", RunKind.Triage),
        ("triage_blocked", RunKind.Triage),
        ("triage_in_progress", RunKind.Triage),
        ("triage_failed", null),
        ("queued", RunKind.Fresh),
        ("blocked", RunKind.Fresh),
        ("revision_queued (review feedback)", RunKind.ReviewFix),
        ("revision_queued (failed checks)", RunKind.ChecksFix),
        ("revision_queued (rebase conflict)", RunKind.ConflictFix),
        ("revision_queued (review findings)", RunKind.FindingsFix),
        ("revision_queued (input provided)", RunKind.InputResume),
        ("continuation_queued", RunKind.Continuation),
        ("revision_blocked", RunKind.ReviewFix),
        ("continuation_blocked", RunKind.Continuation),
        ("in_progress (fresh)", RunKind.Fresh),
        ("in_progress (continuation)", RunKind.Continuation),
        ("revision_in_progress (review feedback)", RunKind.ReviewFix),
        ("revision_in_progress (failed checks)", RunKind.ChecksFix),
        ("revision_in_progress (rebase conflict)", RunKind.ConflictFix),
        ("revision_in_progress (review findings)", RunKind.FindingsFix),
        ("revision_in_progress (input provided)", RunKind.InputResume),
        ("rebase_pending", null),
        ("checks_pending", null),
        ("merge_pending", null),
        ("failed", null),
        ("continuable_failed", null),
        ("revision_failed", null),
        ("unchanged", null),
        ("publish_failed", null),
        ("publish_blocked", null),
        ("branch_diverged", null),
        ("review_queued", RunKind.Review),
        ("review_in_progress", RunKind.Review),
        ("review_failed", null),
        ("awaiting_input", null),
        ("review", null),
        ("completed", null),
    ];

    public static TheoryData<string, RunKind?> RunKinds => new(Expected.Select(e => (e.State, e.RunKind)));

    [Theory]
    [MemberData(nameof(RunKinds))]
    public void WhenInState_DeclaresItsRunKind(string state, RunKind? runKind)
    {
        // Arrange
        Issue issue = States[state]();

        // Act
        RunKind? actual = issue.RunKind;

        // Assert
        actual.ShouldBe(runKind);
    }

    [Fact]
    public void WhenEveryStateTypeIsListed_EachHasAnExpectedRunKind()
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
