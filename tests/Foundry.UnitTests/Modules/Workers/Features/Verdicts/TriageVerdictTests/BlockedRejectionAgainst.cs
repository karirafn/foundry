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
}
