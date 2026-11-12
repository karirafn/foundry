using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Workers.Contracts;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.States.TriageBlockedIssueTests;

public sealed class FailBlockerLink
{
    private static readonly DateTimeOffset DecidedAt = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset FailedAt = new(2026, 11, 11, 12, 5, 0, TimeSpan.Zero);

    private const string RejectionPrefix = "The tracker rejected linking #15 as a blocker: ";

    private static TriageVerdict.Blocked VerdictNaming(params int[] issueNumbers) =>
        new(
            IssueKind.Feature,
            [.. issueNumbers.Select((int number) => new TriageBlocker(number, "Needs it first."))],
            DecidedAt);

    [Fact]
    public void WhenTheTrackerRejectsALink_FailsTriageNamingTheBlockerAndTheRejection()
    {
        // Arrange
        TriageBlockedIssue blocked = new IssueBuilder().TriageBlockedFromTriage(VerdictNaming(15));
        WorkerRunId runId = WorkerRunId.New();

        // Act
        TriageFailedIssue failed = blocked.FailBlockerLink(runId, 15, "issue is locked", FailedAt);

        // Assert
        failed.ShouldSatisfyAllConditions(
            () => failed.FailureCategory.ShouldBe(FailureCategory.BlockerLinkFailed),
            () => failed.FailureReason.ShouldBe("The tracker rejected linking #15 as a blocker: issue is locked"),
            () => failed.WorkerRunId.ShouldBe(runId),
            () => failed.FailedAt.ShouldBe(FailedAt));
    }

    [Fact]
    public void WhenTheRejectionIsLongAndMultiline_RecordsASingleLineWithinTheFailureReasonLimit()
    {
        // Arrange
        TriageBlockedIssue blocked = new IssueBuilder().TriageBlockedFromTriage(VerdictNaming(15));
        string rejection = "first line\n" + new string('x', 2000);

        // Act
        TriageFailedIssue failed = blocked.FailBlockerLink(WorkerRunId.New(), 15, rejection, FailedAt);

        // Assert
        failed.FailureReason.ShouldSatisfyAllConditions(
            () => failed.FailureReason.Length.ShouldBeLessThanOrEqualTo(TriageBlockedIssue.FailureReasonMaxLength),
            () => failed.FailureReason.ShouldNotContain('\n'),
            () => failed.FailureReason.ShouldStartWith("The tracker rejected linking #15 as a blocker: first line x"));
    }

    [Fact]
    public void WhenTheFailureReasonIsExactlyAtTheLimit_KeepsItWhole()
    {
        // Arrange
        TriageBlockedIssue blocked = new IssueBuilder().TriageBlockedFromTriage(VerdictNaming(15));
        string rejection = new('x', TriageBlockedIssue.FailureReasonMaxLength - RejectionPrefix.Length);

        // Act
        TriageFailedIssue failed = blocked.FailBlockerLink(WorkerRunId.New(), 15, rejection, FailedAt);

        // Assert
        failed.FailureReason.ShouldSatisfyAllConditions(
            () => failed.FailureReason.Length.ShouldBe(TriageBlockedIssue.FailureReasonMaxLength),
            () => failed.FailureReason.ShouldEndWith("x"));
    }

    [Fact]
    public void WhenTheFailureReasonIsOneOverTheLimit_CapsItWithAnEllipsis()
    {
        // Arrange
        TriageBlockedIssue blocked = new IssueBuilder().TriageBlockedFromTriage(VerdictNaming(15));
        string rejection = new('x', TriageBlockedIssue.FailureReasonMaxLength - RejectionPrefix.Length + 1);

        // Act
        TriageFailedIssue failed = blocked.FailBlockerLink(WorkerRunId.New(), 15, rejection, FailedAt);

        // Assert
        failed.FailureReason.ShouldSatisfyAllConditions(
            () => failed.FailureReason.Length.ShouldBe(TriageBlockedIssue.FailureReasonMaxLength),
            () => failed.FailureReason.ShouldEndWith("x…"));
    }

    [Fact]
    public void WhenTheCutWouldSplitASurrogatePair_DropsTheHalfPairBeforeTheEllipsis()
    {
        // Arrange
        TriageBlockedIssue blocked = new IssueBuilder().TriageBlockedFromTriage(VerdictNaming(15));
        int lastKeptIndex = TriageBlockedIssue.FailureReasonMaxLength - 2;
        string rejection = new string('x', lastKeptIndex - RejectionPrefix.Length) + "\U0001F600" + "yyy";

        // Act
        TriageFailedIssue failed = blocked.FailBlockerLink(WorkerRunId.New(), 15, rejection, FailedAt);

        // Assert
        failed.FailureReason.ShouldSatisfyAllConditions(
            () => char.IsSurrogate(failed.FailureReason[^2]).ShouldBeFalse(),
            () => failed.FailureReason.ShouldEndWith("x…"));
    }

    [Fact]
    public void WhenTheTrackerRejectsALink_KeepsTheBlockersAndTheVerdict()
    {
        // Arrange
        TriageVerdict.Blocked verdict = VerdictNaming(15, 31);
        TriageBlockedIssue blocked = new IssueBuilder().TriageBlockedFromTriage(verdict);

        // Act
        TriageFailedIssue failed = blocked.FailBlockerLink(WorkerRunId.New(), 15, "issue is locked", FailedAt);

        // Assert
        failed.ShouldSatisfyAllConditions(
            () => failed.BlockedBy.ShouldBe([15, 31]),
            () => failed.TriageVerdict.ShouldBe(verdict));
    }

    [Fact]
    public void WhenTheTrackerRejectsALink_RaisesIssueTriageFailed()
    {
        // Arrange
        TriageBlockedIssue blocked = new IssueBuilder().TriageBlockedFromTriage(VerdictNaming(15));

        // Act
        blocked.FailBlockerLink(WorkerRunId.New(), 15, "issue is locked", FailedAt);

        // Assert
        blocked.DomainEvents.OfType<IssueTriageFailed>().ShouldHaveSingleItem();
    }
}
