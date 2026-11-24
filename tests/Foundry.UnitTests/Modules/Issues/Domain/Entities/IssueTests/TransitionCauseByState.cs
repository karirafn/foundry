using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.IssueTests;

/// <summary>
/// Every state type declares the cause it was entered for: a failure its category, a revision or publish its trigger,
/// a pending or diverged state its reason, a review its newest outcome (review, then merge, then checks), and plain
/// progress none. A state type with no row here fails <see cref="WhenEveryStateTypeIsListed_EachHasAnExpectedCause"/>.
/// </summary>
public sealed class TransitionCauseByState
{
    private static readonly IReadOnlyList<(string State, Func<Issue> Build, string? Cause)> Rows =
    [
        ("detected", () => new IssueBuilder().Detected(), null),
        ("triage_queued", () => new IssueBuilder().TriageQueued(), null),
        ("triage_blocked", () => new IssueBuilder().TriageBlocked([2]), null),
        ("triage_in_progress", () => new IssueBuilder().TriageInProgress(), null),
        ("triage_failed", () => new IssueBuilder().WithFailureCategory(FailureCategory.TimedOut).TriageFailed(), "timed_out"),
        ("queued", () => new IssueBuilder().FreshQueued(), null),
        ("blocked", () => new IssueBuilder().WithBlockedBy([2]).TriageFailed().SkipTriage(DateTimeOffset.UtcNow), null),
        ("revision_queued (feedback)", () => new IssueBuilder().RevisionQueued(), "review_feedback"),
        ("revision_queued (findings)", () => new IssueBuilder().RevisionQueuedFromReviewFindings(), "review_findings"),
        ("revision_queued (checks)", () => new IssueBuilder().ChecksPending().FixChecks([new FailingCheck("ci/build", "failure", null)], DateTimeOffset.UtcNow), "failed_checks"),
        ("continuation_queued", () => new IssueBuilder().ContinuableFailed().Retry(DateTimeOffset.UtcNow), null),
        ("in_progress", () => new IssueBuilder().InProgress(), null),
        ("revision_in_progress", () => new IssueBuilder().RevisionInProgress(), "review_feedback"),
        ("rebase_pending (after run)", () => new IssueBuilder().RebasePending(), "after_run"),
        ("rebase_pending (catch up)", () => new IssueBuilder().RebasePendingForCatchUp(), "catch_up"),
        ("checks_pending (after run)", () => new IssueBuilder().ChecksPending(), "after_run"),
        ("checks_pending (catch up)", () => new IssueBuilder().RebasePendingForCatchUp().MarkChecksPending(new RebaseOutcome.UpToDate(), DateTimeOffset.UtcNow), "catch_up"),
        ("merge_pending", () => new IssueBuilder().MergePending(), "passed"),
        ("failed", () => new IssueBuilder().WithFailureCategory(FailureCategory.TimedOut).Failed(), "timed_out"),
        ("continuable_failed", () => new IssueBuilder().WithFailureCategory(FailureCategory.UsageLimited).ContinuableFailed(), "usage_limited"),
        ("revision_failed", () => new IssueBuilder().WithFailureCategory(FailureCategory.TurnLimit).RevisionFailed(), "turn_limit"),
        ("unchanged", () => new IssueBuilder().Unchanged(), null),
        ("publish_failed (implement)", () => new IssueBuilder().PublishFailed(), null),
        ("publish_failed (revision)", () => new IssueBuilder().PublishFailedFromRevision(), "review_feedback"),
        ("publish_blocked (implement)", () => new IssueBuilder().PublishBlocked(), null),
        ("publish_blocked (revision)", () => new IssueBuilder().PublishBlockedFromRevision(), "review_feedback"),
        ("branch_diverged (moved)", () => new IssueBuilder().BranchDiverged(), "branch-moved"),
        ("branch_diverged (protected)", () => new IssueBuilder().BranchDiverged(reason: BranchDivergenceReason.ForcePushProtected), "force-push-protected"),
        ("review_queued", () => new IssueBuilder().WithChecksOutcome(new ChecksOutcome.NoneDetected()).ReviewQueued(), "none_detected"),
        ("review_in_progress", () => new IssueBuilder().ReviewInProgress(), "passed"),
        ("review_failed", () => new IssueBuilder().ReviewFailed(), "transient_api_error"),
        ("awaiting_input", () => new IssueBuilder().AwaitingInput(), null),
        ("review (checks)", () => new IssueBuilder().WithChecksOutcome(new ChecksOutcome.Unfinished()).Review(), "unfinished"),
        ("review (review skipped)", () => new IssueBuilder().ReviewSkipped(), "skipped"),
        ("completed", () => new IssueBuilder().Completed(), null),
    ];

    public static TheoryData<string, string?> Expected => new(Rows.Select(r => (r.State, r.Cause)));

    [Theory]
    [MemberData(nameof(Expected))]
    public void WhenStateIsEntered_ItsTransitionCauseIsTheTablesCause(string state, string? cause)
    {
        // Arrange
        Func<Issue> build = Rows.Single(r => r.State == state).Build;

        // Act
        Issue issue = build();

        // Assert
        issue.TransitionCause.ShouldBe(cause);
    }

    [Fact]
    public void WhenEveryStateTypeIsListed_EachHasAnExpectedCause()
    {
        // Arrange
        IReadOnlyList<Type> stateTypes = typeof(Issue).Assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(Issue)) && !t.IsAbstract)
            .ToList();

        // Act
        IReadOnlyList<Type> covered = Rows.Select(r => r.Build().GetType()).Distinct().ToList();

        // Assert
        stateTypes.Except(covered).ShouldBeEmpty();
    }
}
