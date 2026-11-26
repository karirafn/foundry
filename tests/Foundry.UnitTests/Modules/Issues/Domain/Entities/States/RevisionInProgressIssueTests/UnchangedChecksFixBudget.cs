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

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.States.RevisionInProgressIssueTests;

/// <summary>
/// A checks-fix run that pushes no commit spends the rest of the fix budget (issue #1140): every new fix budget
/// follows a push, so a red verdict on unchanged code goes to review instead of a second fix run on the same inputs.
/// </summary>
public sealed class UnchangedChecksFixBudget
{
    private const string Head = "4f2c9e1a7b3d5f6e8a0c2b4d6f8e0a1c3b5d7f9e";

    private static readonly IReadOnlyList<FailingCheck> Failing =
    [
        new FailingCheck("ci/build", "failure", "https://github.com/owner/repo/actions/runs/1"),
    ];

    [Fact]
    public void WhenFailedChecksRunPushesNoCommit_ExhaustsTheFixBudget()
    {
        // Arrange
        RevisionInProgressIssue fixRun = FirstFixRun();

        // Act
        RebasePendingIssue rebasePending = fixRun.MarkUnchangedRebasePending(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

        // Assert
        rebasePending.ChecksFixAttempts.ShouldBe(ChecksPendingIssue.ChecksMaxFixPushes);
    }

    [Fact]
    public void WhenFailedChecksRunPushesNoCommit_AndChecksStillFail_GoesToReviewWithTheOutcome()
    {
        // Arrange
        RebasePendingIssue rebasePending = FirstFixRun().MarkUnchangedRebasePending(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        ChecksPendingIssue checksPending = rebasePending
            .ResolveRebase(new RebaseOutcome.Rebased(), DateTimeOffset.UtcNow)
            .ShouldBeOfType<ChecksPendingIssue>();
        ChecksOutcome.StillFailing stillFailing = new(Failing);

        // Act
        Issue next = checksPending.ResolveVerdict(stillFailing, autoMerge: true, reviewPullRequests: false, Head, DateTimeOffset.UtcNow);

        // Assert
        ReviewIssue review = next.ShouldBeOfType<ReviewIssue>();
        review.ChecksOutcome.ShouldBe(stillFailing);
    }

    [Fact]
    public void WhenFailedChecksRunPushesNoCommit_AndChecksPassWithAutoMerge_GoesToMergePending()
    {
        // Arrange
        ChecksPendingIssue checksPending = FirstFixRun()
            .MarkUnchangedRebasePending(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)
            .MarkChecksPending(new RebaseOutcome.Rebased(), DateTimeOffset.UtcNow);

        // Act
        Issue next = checksPending.ResolveVerdict(new ChecksOutcome.Passed(), autoMerge: true, reviewPullRequests: false, Head, DateTimeOffset.UtcNow);

        // Assert
        next.ShouldBeOfType<MergePendingIssue>();
    }

    [Fact]
    public void WhenFailedChecksRunPushesNoCommit_AndChecksPassWithoutAutoMerge_RebasesBeforeReady()
    {
        // Arrange
        ChecksPendingIssue checksPending = FirstFixRun()
            .MarkUnchangedRebasePending(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)
            .MarkChecksPending(new RebaseOutcome.Rebased(), DateTimeOffset.UtcNow);

        // Act
        Issue next = checksPending.ResolveVerdict(new ChecksOutcome.Passed(), autoMerge: false, reviewPullRequests: false, Head, DateTimeOffset.UtcNow);

        // Assert
        RebasePendingIssue rebasePending = next.ShouldBeOfType<RebasePendingIssue>();
        rebasePending.ShouldSatisfyAllConditions(
            () => rebasePending.Reason.ShouldBeOfType<RebasePendingReason.BeforeReady>(),
            () => rebasePending.ChecksOutcome.ShouldBeOfType<ChecksOutcome.Passed>());
    }

    [Fact]
    public void WhenFailedChecksRunPushesACommit_CarriesTheFixAttempts()
    {
        // Arrange
        RevisionInProgressIssue fixRun = FirstFixRun();

        // Act
        RebasePendingIssue rebasePending = fixRun.MarkRebasePending(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

        // Assert
        rebasePending.ChecksFixAttempts.ShouldBe(1);
    }

    [Fact]
    public void WhenRebaseConflictRunPushesNoCommit_CarriesTheFixAttempts()
    {
        // Arrange
        RevisionInProgressIssue conflictRun = new IssueBuilder()
            .WithFailingChecks(Failing)
            .ChecksPending()
            .FixChecks(Failing, DateTimeOffset.UtcNow)
            .Claim(WorkerRunId.New(), DateTimeOffset.UtcNow)
            .MarkRebasePending(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)
            .ResolveConflict(DateTimeOffset.UtcNow)
            .Claim(WorkerRunId.New(), DateTimeOffset.UtcNow);
        conflictRun.Trigger.ShouldBeOfType<RevisionTrigger.RebaseConflict>();

        // Act
        RebasePendingIssue rebasePending = conflictRun.MarkUnchangedRebasePending(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

        // Assert
        rebasePending.ChecksFixAttempts.ShouldBe(1);
    }

    [Fact]
    public void WhenReviewFeedbackRunPushesNoCommit_CarriesTheFixAttempts()
    {
        // Arrange
        RevisionInProgressIssue feedbackRun = new IssueBuilder().RevisionInProgress();
        feedbackRun.Trigger.ShouldBeOfType<RevisionTrigger.ReviewFeedback>();

        // Act
        RebasePendingIssue rebasePending = feedbackRun.MarkUnchangedRebasePending(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

        // Assert
        rebasePending.ChecksFixAttempts.ShouldBe(0);
    }

    [Fact]
    public void WhenInputProvidedRunPushesNoCommit_CarriesTheFixAttempts()
    {
        // Arrange
        AwaitingInputIssue awaiting = new IssueBuilder().AwaitingInputPosted(DateTimeOffset.UtcNow.AddHours(-2));
        RevisionInProgressIssue inputRun = ((RevisionQueuedIssue)awaiting
                .ProvideInput(ClarificationInput.FromComments("Use Postgres."), DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow))
            .Claim(WorkerRunId.New(), DateTimeOffset.UtcNow);
        inputRun.Trigger.ShouldBeOfType<RevisionTrigger.InputProvided>();

        // Act
        RebasePendingIssue rebasePending = inputRun.MarkUnchangedRebasePending(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

        // Assert
        rebasePending.ChecksFixAttempts.ShouldBe(0);
    }

    private static RevisionInProgressIssue FirstFixRun()
    {
        RevisionInProgressIssue fixRun = new IssueBuilder()
            .ChecksPending()
            .FixChecks(Failing, DateTimeOffset.UtcNow)
            .Claim(WorkerRunId.New(), DateTimeOffset.UtcNow);
        fixRun.Trigger.ShouldBeOfType<RevisionTrigger.FailedChecks>();
        fixRun.ChecksFixAttempts.ShouldBe(1);
        return fixRun;
    }
}
