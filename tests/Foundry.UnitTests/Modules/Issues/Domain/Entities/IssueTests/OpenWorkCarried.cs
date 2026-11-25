using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.IssueTests;

public sealed class OpenWorkCarried
{
    private static readonly DateTimeOffset Now = new(2026, 11, 25, 10, 0, 0, TimeSpan.Zero);

    private static readonly OpenWork Work = OpenWork.Of("feat/7-add-retry", "https://github.com/o/r/pull/9");

    private static TriageQueuedIssue HoldingWork() =>
        TriageQueuedIssue.HoldingOpenWork(new IssueBuilder().TriageQueued(), Work, Now);

    private static TriageInProgressIssue TriagingWithWork() => HoldingWork().Claim(WorkerRunId.New(), Now);

    [Fact]
    public void WhenAnIssueGoesBackToTriageHoldingOpenWork_TriageQueuedHoldsIt()
    {
        // Act
        TriageQueuedIssue queued = HoldingWork();

        // Assert
        queued.OpenWork.ShouldBe(Work);
    }

    [Fact]
    public void WhenAnIssueNeverHadOpenWork_ItHasNone()
    {
        // Act
        TriageQueuedIssue queued = new IssueBuilder().TriageQueued();

        // Assert
        queued.OpenWork.ShouldBeNull();
    }

    [Fact]
    public void WhenTriageQueuedIsClaimed_TriageInProgressKeepsIt()
    {
        // Act
        TriageInProgressIssue triaging = TriagingWithWork();

        // Assert
        triaging.OpenWork.ShouldBe(Work);
    }

    [Fact]
    public void WhenTriageQueuedGainsBlockers_TriageBlockedKeepsIt()
    {
        // Act
        TriageBlockedIssue blocked = HoldingWork().Block([3], Now);

        // Assert
        blocked.OpenWork.ShouldBe(Work);
    }

    [Fact]
    public void WhenTriageBlockedUnblocks_TriageQueuedKeepsIt()
    {
        // Act
        TriageQueuedIssue queued = HoldingWork().Block([3], Now).Unblock(Now);

        // Assert
        queued.OpenWork.ShouldBe(Work);
    }

    [Fact]
    public void WhenTriageNamesBlockers_TriageBlockedKeepsIt()
    {
        // Act
        TriageBlockedIssue blocked = TriagingWithWork().MarkBlocked(
            new TriageVerdict.Blocked(IssueKind.Feature, [new TriageBlocker(3, "Needs the API first.")], Now),
            Now);

        // Assert
        blocked.OpenWork.ShouldBe(Work);
    }

    [Fact]
    public void WhenTriageFails_TriageFailedKeepsIt()
    {
        // Act
        TriageFailedIssue failed = TriagingWithWork().MarkFailed("boom", FailureCategory.VerdictInvalid, Now);

        // Assert
        failed.OpenWork.ShouldBe(Work);
    }

    [Fact]
    public void WhenFailedTriageIsRetried_TriageQueuedKeepsIt()
    {
        // Act
        TriageQueuedIssue queued = TriagingWithWork().MarkFailed("boom", FailureCategory.VerdictInvalid, Now).RetryTriage(Now);

        // Assert
        queued.OpenWork.ShouldBe(Work);
    }

    [Fact]
    public void WhenTriageNeedsInput_AwaitingInputWithARetriageTargetKeepsIt()
    {
        // Act
        AwaitingInputIssue awaiting = TriagingWithWork().MarkNeedsInput(
            new TriageVerdict.NeedsInput(IssueKind.Feature, IssueBuilder.DefaultQuestions, Now),
            [],
            Now);

        // Assert
        awaiting.ShouldSatisfyAllConditions(
            () => awaiting.ResumeTarget.ShouldBeOfType<ResumeTarget.Retriage>(),
            () => awaiting.OpenWork.ShouldBe(Work));
    }

    [Fact]
    public void WhenTheRetriageQuestionIsAnswered_TriageQueuedKeepsIt()
    {
        // Arrange
        AwaitingInputIssue awaiting = TriagingWithWork().MarkNeedsInput(
            new TriageVerdict.NeedsInput(IssueKind.Feature, IssueBuilder.DefaultQuestions, Now),
            [],
            Now);
        awaiting.RecordQuestionPosted(1, Now, null);

        // Act
        TriageQueuedIssue queued = awaiting
            .ProvideInput(ClarificationInput.FromComments("Use Postgres."), Now.AddHours(1), Now.AddHours(1))
            .ShouldBeOfType<TriageQueuedIssue>();

        // Assert
        queued.OpenWork.ShouldBe(Work);
    }
}
