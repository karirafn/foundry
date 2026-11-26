using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.States.ChecksPendingIssueTests;

public sealed class ResolveVerdict
{
    private const string Head = "4f2c9e1a7b3d5f6e8a0c2b4d6f8e0a1c3b5d7f9e";

    private static readonly IReadOnlyList<FailingCheck> Failing =
    [
        new FailingCheck("ci/build", "failure", "https://github.com/owner/repo/actions/runs/1"),
    ];

    [Fact]
    public void WhenPassedAndAutoMerge_ReturnsMergePendingPinnedToHead()
    {
        // Arrange
        RebaseOutcome rebaseOutcome = new RebaseOutcome.Rebased();
        ChecksPendingIssue checksPending = new IssueBuilder().WithRebaseOutcome(rebaseOutcome).ChecksPending();
        DateTimeOffset now = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);

        // Act
        Issue next = checksPending.ResolveVerdict(new ChecksOutcome.Passed(), autoMerge: true, reviewPullRequests: false, Head, now);

        // Assert
        MergePendingIssue mergePending = next.ShouldBeOfType<MergePendingIssue>();
        mergePending.ShouldSatisfyAllConditions(
            () => mergePending.Id.ShouldBe(checksPending.Id),
            () => mergePending.HeadSha.ShouldBe(Head),
            () => mergePending.WorkerRunId.ShouldBe(checksPending.WorkerRunId),
            () => mergePending.BranchName.ShouldBe(checksPending.BranchName),
            () => mergePending.PullRequestUrl.ShouldBe(checksPending.PullRequestUrl),
            () => mergePending.FeedbackCutoffAt.ShouldBe(checksPending.FeedbackCutoffAt),
            () => mergePending.ChecksOutcome.ShouldBeOfType<ChecksOutcome.Passed>(),
            () => mergePending.RebaseOutcome.ShouldBe(rebaseOutcome),
            () => mergePending.EnteredMergePendingAt.ShouldBe(now),
            () => mergePending.LifecycleStep.ShouldBe(IssueLifecycleStep.Merge),
            () => mergePending.NeedsAction.ShouldBeFalse());
    }

    [Fact]
    public void WhenPassedAndAutoMerge_RaisesIssueMergePending()
    {
        // Arrange
        ChecksPendingIssue checksPending = new IssueBuilder().WithIssueNumber(7).ChecksPending();
        DateTimeOffset now = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);

        // Act
        checksPending.ResolveVerdict(new ChecksOutcome.Passed(), autoMerge: true, reviewPullRequests: false, Head, now);

        // Assert
        IssueMergePending raised = checksPending.DomainEvents.OfType<IssueMergePending>().ShouldHaveSingleItem();
        raised.ShouldSatisfyAllConditions(
            () => raised.IssueNumber.ShouldBe(7),
            () => raised.EnteredMergePendingAt.ShouldBe(now));
    }

    [Fact]
    public void WhenPassedWithoutAutoMerge_RebasesBeforeReadyVerifiedAtHead()
    {
        // Arrange
        ChecksPendingIssue checksPending = new IssueBuilder().ChecksPending();

        // Act
        Issue next = checksPending.ResolveVerdict(new ChecksOutcome.Passed(), autoMerge: false, reviewPullRequests: false, Head, DateTimeOffset.UtcNow);

        // Assert
        RebasePendingIssue rebasePending = next.ShouldBeOfType<RebasePendingIssue>();
        rebasePending.ShouldSatisfyAllConditions(
            () => rebasePending.Reason.ShouldBeOfType<RebasePendingReason.BeforeReady>(),
            () => rebasePending.VerifiedHeadSha.ShouldBe(Head));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void WhenPassedAndAutoMergeWithoutHead_ReturnsReview(string? head)
    {
        // Arrange
        ChecksPendingIssue checksPending = new IssueBuilder().ChecksPending();

        // Act
        Issue next = checksPending.ResolveVerdict(new ChecksOutcome.Passed(), autoMerge: true, reviewPullRequests: false, head, DateTimeOffset.UtcNow);

        // Assert
        next.ShouldBeOfType<ReviewIssue>();
    }

    [Fact]
    public void WhenPassedAndAutoMergeAfterPersonFeedback_RebasesBeforeReadyWithPassed()
    {
        // Arrange
        ChecksPendingIssue checksPending = new IssueBuilder()
            .WithReviewComments([new ReviewComment("Please rename this.")])
            .ChecksPendingFromRevision();

        // Act
        Issue next = checksPending.ResolveVerdict(new ChecksOutcome.Passed(), autoMerge: true, reviewPullRequests: false, Head, DateTimeOffset.UtcNow);

        // Assert
        RebasePendingIssue rebasePending = next.ShouldBeOfType<RebasePendingIssue>();
        rebasePending.ShouldSatisfyAllConditions(
            () => rebasePending.Reason.ShouldBeOfType<RebasePendingReason.BeforeReady>(),
            () => rebasePending.ChecksOutcome.ShouldBeOfType<ChecksOutcome.Passed>(),
            () => rebasePending.HasPersonFeedback.ShouldBeTrue(),
            () => checksPending.DomainEvents.OfType<IssueMergePending>().ShouldBeEmpty());
    }

    [Fact]
    public void WhenNoneDetectedAndAutoMerge_RebasesBeforeReadyWithTheOutcome()
    {
        // Arrange
        ChecksPendingIssue checksPending = new IssueBuilder().ChecksPending();

        // Act
        Issue next = checksPending.ResolveVerdict(new ChecksOutcome.NoneDetected(), autoMerge: true, reviewPullRequests: false, Head, DateTimeOffset.UtcNow);

        // Assert
        RebasePendingIssue rebasePending = next.ShouldBeOfType<RebasePendingIssue>();
        rebasePending.ShouldSatisfyAllConditions(
            () => rebasePending.Reason.ShouldBeOfType<RebasePendingReason.BeforeReady>(),
            () => rebasePending.ChecksOutcome.ShouldBe(new ChecksOutcome.NoneDetected()));
    }

    public static TheoryData<ChecksOutcome> NotPassedOutcomes() =>
    [
        new ChecksOutcome.Unfinished(),
        new ChecksOutcome.Unreadable(),
        new ChecksOutcome.StillFailing([]),
    ];

    [Theory]
    [MemberData(nameof(NotPassedOutcomes))]
    public void WhenNotPassedAndAutoMerge_ReturnsReviewWithTheOutcome(ChecksOutcome outcome)
    {
        // Arrange
        ChecksPendingIssue checksPending = new IssueBuilder().ChecksPending();

        // Act
        Issue next = checksPending.ResolveVerdict(outcome, autoMerge: true, reviewPullRequests: false, Head, DateTimeOffset.UtcNow);

        // Assert
        next.ShouldBeOfType<ReviewIssue>().ChecksOutcome.ShouldBe(outcome);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void WhenStillFailingUnderCap_ReturnsChecksFixRevision(bool autoMerge)
    {
        // Arrange
        ChecksPendingIssue checksPending = new IssueBuilder().ChecksPending();

        // Act
        Issue next = checksPending.ResolveVerdict(new ChecksOutcome.StillFailing(Failing), autoMerge, reviewPullRequests: false, Head, DateTimeOffset.UtcNow);

        // Assert
        RevisionQueuedIssue revision = next.ShouldBeOfType<RevisionQueuedIssue>();
        revision.Trigger.ShouldBeOfType<RevisionTrigger.FailedChecks>().FailingChecks.ShouldBe(Failing);
    }

    [Fact]
    public void WhenStillFailingAtCap_ReturnsReview()
    {
        // Arrange
        ChecksPendingIssue checksPending = new IssueBuilder().WithFailingChecks(Failing).ChecksPendingAtCap();

        // Act
        Issue next = checksPending.ResolveVerdict(new ChecksOutcome.StillFailing(Failing), autoMerge: true, reviewPullRequests: false, Head, DateTimeOffset.UtcNow);

        // Assert
        next.ShouldBeOfType<ReviewIssue>().ChecksOutcome.ShouldBeOfType<ChecksOutcome.StillFailing>();
    }

    [Fact]
    public void WhenEveryFailingCheckIsAnInfrastructureFailure_ReturnsReviewCarryingEveryCheck()
    {
        // Arrange
        IReadOnlyList<FailingCheck> infrastructure =
        [
            new FailingCheck("build", "ci_quota_exceeded", "https://gitlab.com/group/project/-/jobs/1", IsInfrastructureFailure: true),
            new FailingCheck("test", "runner_system_failure", null, IsInfrastructureFailure: true),
        ];
        ChecksPendingIssue checksPending = new IssueBuilder().ChecksPending();

        // Act
        Issue next = checksPending.ResolveVerdict(
            new ChecksOutcome.StillFailing(infrastructure), autoMerge: true, reviewPullRequests: false, Head, DateTimeOffset.UtcNow);

        // Assert
        next.ShouldBeOfType<ReviewIssue>().ChecksOutcome
            .ShouldBeOfType<ChecksOutcome.StillFailing>().FailingChecks.ShouldBe(infrastructure);
    }

    [Fact]
    public void WhenEveryFailingCheckIsAnInfrastructureFailure_QueuesNoRevision()
    {
        // Arrange
        ChecksPendingIssue checksPending = new IssueBuilder().ChecksPending();

        // Act
        checksPending.ResolveVerdict(
            new ChecksOutcome.StillFailing([new FailingCheck("build", "ci_quota_exceeded", null, IsInfrastructureFailure: true)]),
            autoMerge: false,
            reviewPullRequests: false,
            Head,
            DateTimeOffset.UtcNow);

        // Assert
        checksPending.DomainEvents.OfType<IssueRevisionQueued>().ShouldBeEmpty();
    }

    [Fact]
    public void WhenInfrastructureFailed_ReturnsReviewCarryingTheOutcome()
    {
        // Arrange
        ChecksOutcome.InfrastructureFailed outcome = new(
            [new FailingCheck("build", "ci_quota_exceeded", "https://gitlab.com/group/project/-/jobs/1", IsInfrastructureFailure: true)]);
        ChecksPendingIssue checksPending = new IssueBuilder().ChecksPending();

        // Act
        Issue next = checksPending.ResolveVerdict(outcome, autoMerge: true, reviewPullRequests: false, Head, DateTimeOffset.UtcNow);

        // Assert
        next.ShouldBeOfType<ReviewIssue>().ChecksOutcome.ShouldBe(outcome);
    }

    [Fact]
    public void WhenInfrastructureFailed_QueuesNoRevisionAndSpendsNoFixAttempt()
    {
        // Arrange
        ChecksPendingIssue checksPending = new IssueBuilder().ChecksPending();
        int attemptsBefore = checksPending.ChecksFixAttempts;

        // Act
        checksPending.ResolveVerdict(
            new ChecksOutcome.InfrastructureFailed([new FailingCheck("build", "ci_quota_exceeded", null, IsInfrastructureFailure: true)]),
            autoMerge: false,
            reviewPullRequests: false,
            Head,
            DateTimeOffset.UtcNow);

        // Assert
        checksPending.ShouldSatisfyAllConditions(
            () => checksPending.DomainEvents.OfType<IssueRevisionQueued>().ShouldBeEmpty(),
            () => checksPending.ChecksFixAttempts.ShouldBe(attemptsBefore));
    }

    [Fact]
    public void WhenFixableAndInfrastructureFailuresMixUnderCap_QueuesARevisionNamingOnlyTheFixableChecks()
    {
        // Arrange
        FailingCheck fixable = new("test", "script_failure", "https://gitlab.com/group/project/-/jobs/2");
        FailingCheck infrastructure = new("build", "ci_quota_exceeded", null, IsInfrastructureFailure: true);
        ChecksPendingIssue checksPending = new IssueBuilder().ChecksPending();

        // Act
        Issue next = checksPending.ResolveVerdict(
            new ChecksOutcome.StillFailing([infrastructure, fixable]), autoMerge: false, reviewPullRequests: false, Head, DateTimeOffset.UtcNow);

        // Assert
        RevisionQueuedIssue revision = next.ShouldBeOfType<RevisionQueuedIssue>();
        revision.ShouldSatisfyAllConditions(
            () => revision.Trigger.ShouldBeOfType<RevisionTrigger.FailedChecks>().FailingChecks.ShouldBe([fixable]),
            () => revision.ChecksFixAttempts.ShouldBe(checksPending.ChecksFixAttempts + 1));
    }

    [Fact]
    public void WhenFixableAndInfrastructureFailuresMixAtCap_ReturnsReviewCarryingEveryCheck()
    {
        // Arrange
        IReadOnlyList<FailingCheck> mixed =
        [
            new FailingCheck("test", "script_failure", null),
            new FailingCheck("build", "ci_quota_exceeded", null, IsInfrastructureFailure: true),
        ];
        ChecksPendingIssue checksPending = new IssueBuilder().WithFailingChecks(mixed).ChecksPendingAtCap();

        // Act
        Issue next = checksPending.ResolveVerdict(new ChecksOutcome.StillFailing(mixed), autoMerge: true, reviewPullRequests: false, Head, DateTimeOffset.UtcNow);

        // Assert
        next.ShouldBeOfType<ReviewIssue>().ChecksOutcome
            .ShouldBeOfType<ChecksOutcome.StillFailing>().FailingChecks.ShouldBe(mixed);
    }
}
