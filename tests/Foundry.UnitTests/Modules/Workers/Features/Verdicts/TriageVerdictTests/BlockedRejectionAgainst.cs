using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Verdicts;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Verdicts.TriageVerdictTests;

public sealed class BlockedRejectionAgainst
{
    [Fact]
    public void WhenAnOverLongReasonNamesAnUnsuppliedIssue_ReportsTheReasonLength()
    {
        // Arrange
        TriageVerdict.Blocked sut = new(
            TriageIssueKind.Bug,
            [new TriageBlocker(99, new string('a', TriageBlocker.ReasonMaxLength + 1))]);

        // Act
        FailureReason.VerdictInvalid? rejection = sut.RejectionAgainst([15]);

        // Assert
        rejection.ShouldBe(FailureReason.VerdictInvalid.BlockerReasonTooLong(99, TriageBlocker.ReasonMaxLength));
    }

    [Fact]
    public void WhenTheRunRecordedNoSuppliedSet_ReportsNoSuppliedSetInsteadOfAnUnsuppliedBlocker()
    {
        // Arrange
        TriageVerdict.Blocked sut = new(TriageIssueKind.Bug, [new TriageBlocker(15, "Needs it first.")]);

        // Act
        FailureReason.VerdictInvalid? rejection = sut.RejectionAgainst(suppliedOpenIssueNumbers: null);

        // Assert
        rejection.ShouldBe(new FailureReason.VerdictInvalid(FailureReason.VerdictInvalid.NoSuppliedSetDetail));
    }

    [Fact]
    public void WhenTheListIsOverCapOnARunWithNoRecordedSet_ReportsTheCap()
    {
        // Arrange
        List<TriageBlocker> blockers = Enumerable
            .Range(1, TriageResult.Blocked.MaxBlockers + 1)
            .Select(number => new TriageBlocker(number, "Needs it first."))
            .ToList();
        TriageVerdict.Blocked sut = new(TriageIssueKind.Bug, blockers);

        // Act
        FailureReason.VerdictInvalid? rejection = sut.RejectionAgainst(suppliedOpenIssueNumbers: null);

        // Assert
        rejection.ShouldBe(FailureReason.VerdictInvalid.BlockersExceedCap(TriageResult.Blocked.MaxBlockers));
    }

    [Fact]
    public void WhenADuplicatedIssueIsAlsoUnsupplied_ReportsItUnsuppliedAtItsFirstOccurrence()
    {
        // Arrange
        TriageVerdict.Blocked sut = new(
            TriageIssueKind.Bug,
            [new TriageBlocker(99, "Needs it first."), new TriageBlocker(99, "Needs it second.")]);

        // Act
        FailureReason.VerdictInvalid? rejection = sut.RejectionAgainst([15]);

        // Assert
        rejection.ShouldBe(FailureReason.VerdictInvalid.BlockerNotSupplied(99));
    }
}
