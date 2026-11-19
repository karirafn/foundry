using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.States.ChecksPendingIssueTests;

/// <summary>The checks gate with the repository's Review Pull Requests setting on (ADR 0113).</summary>
public sealed class ResolveVerdictWithReview
{
    private const string Head = "4f2c9e1a7b3d5f6e8a0c2b4d6f8e0a1c3b5d7f9e";

    public static TheoryData<ChecksOutcome, bool> GreenOutcomes() => new()
    {
        { new ChecksOutcome.Passed(), true },
        { new ChecksOutcome.Passed(), false },
        { new ChecksOutcome.NoneDetected(), true },
        { new ChecksOutcome.NoneDetected(), false },
    };

    [Theory]
    [MemberData(nameof(GreenOutcomes))]
    public void WhenGreenWithAHead_ReturnsReviewQueuedCarryingTheGate(ChecksOutcome outcome, bool autoMerge)
    {
        // Arrange
        WorkerRunId workerRunId = WorkerRunId.New();
        RebaseOutcome rebaseOutcome = new RebaseOutcome.Rebased();
        ChecksPendingIssue checksPending = new IssueBuilder()
            .WithWorkerRunId(workerRunId)
            .WithRebaseOutcome(rebaseOutcome)
            .ChecksPending();

        // Act
        Issue next = checksPending.ResolveVerdict(outcome, autoMerge, reviewPullRequests: true, Head, DateTimeOffset.UtcNow);

        // Assert
        ReviewQueuedIssue queued = next.ShouldBeOfType<ReviewQueuedIssue>();
        queued.ShouldSatisfyAllConditions(
            () => queued.Id.ShouldBe(checksPending.Id),
            () => queued.WorkerRunId.ShouldBe(workerRunId),
            () => queued.BranchName.ShouldBe(checksPending.BranchName),
            () => queued.PullRequestUrl.ShouldBe(checksPending.PullRequestUrl),
            () => queued.FeedbackCutoffAt.ShouldBe(checksPending.FeedbackCutoffAt),
            () => queued.ChecksOutcome.ShouldBe(outcome),
            () => queued.RebaseOutcome.ShouldBe(rebaseOutcome),
            () => queued.RebaseConflictAttempts.ShouldBe(checksPending.RebaseConflictAttempts),
            () => queued.ReviewedCommit.ShouldBe(Head),
            () => queued.AutoMerge.ShouldBe(autoMerge));
    }

    [Fact]
    public void WhenGreen_RaisesIssueReviewQueued()
    {
        // Arrange
        ChecksPendingIssue checksPending = new IssueBuilder().ChecksPending();

        // Act
        checksPending.ResolveVerdict(
            new ChecksOutcome.Passed(),
            autoMerge: true,
            reviewPullRequests: true,
            Head,
            DateTimeOffset.UtcNow);

        // Assert
        checksPending.ShouldSatisfyAllConditions(
            () => checksPending.DomainEvents.OfType<IssueReviewQueued>().ShouldHaveSingleItem(),
            () => checksPending.DomainEvents.OfType<IssueMergePending>().ShouldBeEmpty());
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public void WhenPassedWithoutAHead_ReturnsReviewAsWithoutTheSetting(string? headSha)
    {
        // Arrange
        ChecksPendingIssue checksPending = new IssueBuilder().ChecksPending();

        // Act
        Issue next = checksPending.ResolveVerdict(
            new ChecksOutcome.Passed(),
            autoMerge: true,
            reviewPullRequests: true,
            headSha,
            DateTimeOffset.UtcNow);

        // Assert
        next.ShouldBeOfType<ReviewIssue>();
    }

    [Fact]
    public void WhenStillFailingUnderCap_StillQueuesAChecksFix()
    {
        // Arrange
        ChecksPendingIssue checksPending = new IssueBuilder().ChecksPending();
        IReadOnlyList<FailingCheck> failing = [new FailingCheck("ci/build", "failure", null)];

        // Act
        Issue next = checksPending.ResolveVerdict(
            new ChecksOutcome.StillFailing(failing),
            autoMerge: true,
            reviewPullRequests: true,
            Head,
            DateTimeOffset.UtcNow);

        // Assert
        next.ShouldBeOfType<RevisionQueuedIssue>().Trigger.ShouldBeOfType<RevisionTrigger.FailedChecks>();
    }

    public static TheoryData<ChecksOutcome> NotGreenOutcomes() =>
    [
        new ChecksOutcome.Unfinished(),
        new ChecksOutcome.Unreadable(),
        new ChecksOutcome.InfrastructureFailed([new FailingCheck("build", "ci_quota_exceeded", null, IsInfrastructureFailure: true)]),
    ];

    [Theory]
    [MemberData(nameof(NotGreenOutcomes))]
    public void WhenNotGreen_ReturnsReviewCarryingTheOutcome(ChecksOutcome outcome)
    {
        // Arrange
        ChecksPendingIssue checksPending = new IssueBuilder().ChecksPending();

        // Act
        Issue next = checksPending.ResolveVerdict(outcome, autoMerge: true, reviewPullRequests: true, Head, DateTimeOffset.UtcNow);

        // Assert
        next.ShouldBeOfType<ReviewIssue>().ChecksOutcome.ShouldBe(outcome);
    }
}
