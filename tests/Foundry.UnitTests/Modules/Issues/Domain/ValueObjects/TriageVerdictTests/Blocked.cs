using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Workers.Contracts;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.ValueObjects.TriageVerdictTests;

public sealed class Blocked
{
    private static readonly DateTimeOffset DecidedAt = new(2026, 11, 11, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void WhenBlockersAreNamed_ExposesThemInOrder()
    {
        // Arrange
        TriageBlocker[] blockers = [new TriageBlocker(15, "Needs the retry queue."), new TriageBlocker(31, "Needs the schema.")];

        // Act
        TriageVerdict.Blocked verdict = new(IssueKind.Feature, blockers, DecidedAt);

        // Assert
        verdict.BlockedBy.Select(b => b.IssueNumber).ShouldBe([15, 31]);
    }

    [Fact]
    public void WhenNoBlockersAreNamed_Throws()
    {
        // Arrange
        TriageBlocker[] blockers = [];

        // Act
        ArgumentException exception = Should.Throw<ArgumentException>(() => new TriageVerdict.Blocked(IssueKind.Feature, blockers, DecidedAt));

        // Assert
        exception.ParamName.ShouldBe("blockers");
    }

    [Fact]
    public void WhenAnIssueIsNamed_ReasonForReturnsItsReason()
    {
        // Arrange
        TriageVerdict.Blocked verdict = new(
            IssueKind.Feature,
            [new TriageBlocker(15, "Needs the retry queue."), new TriageBlocker(31, "Needs the schema.")],
            DecidedAt);

        // Act
        string? reason = verdict.ReasonFor(31);

        // Assert
        reason.ShouldBe("Needs the schema.");
    }

    [Fact]
    public void WhenAnIssueIsNotNamed_ReasonForReturnsNull()
    {
        // Arrange
        TriageVerdict.Blocked verdict = new(IssueKind.Feature, [new TriageBlocker(15, "Needs the retry queue.")], DecidedAt);

        // Act
        string? reason = verdict.ReasonFor(99);

        // Assert
        reason.ShouldBeNull();
    }

    [Fact]
    public void WhenBlockersAreEqual_VerdictsAreEqual()
    {
        // Arrange
        TriageVerdict.Blocked first = new(IssueKind.Feature, [new TriageBlocker(15, "Needs the retry queue.")], DecidedAt);
        TriageVerdict.Blocked second = new(IssueKind.Feature, [new TriageBlocker(15, "Needs the retry queue.")], DecidedAt);

        // Act
        bool equal = first.Equals(second);

        // Assert
        equal.ShouldBeTrue();
    }

    [Fact]
    public void WhenABlockerReasonDiffers_VerdictsAreNotEqual()
    {
        // Arrange
        TriageVerdict.Blocked first = new(IssueKind.Feature, [new TriageBlocker(15, "Needs the retry queue.")], DecidedAt);
        TriageVerdict.Blocked second = new(IssueKind.Feature, [new TriageBlocker(15, "Needs the schema.")], DecidedAt);

        // Act
        bool equal = first.Equals(second);

        // Assert
        equal.ShouldBeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void WhenAReasonIsBlank_Throws(string reason)
    {
        // Arrange
        TriageBlocker[] blockers = [new TriageBlocker(15, reason)];

        // Act
        ArgumentException exception = Should.Throw<ArgumentException>(() => new TriageVerdict.Blocked(IssueKind.Feature, blockers, DecidedAt));

        // Assert
        exception.ParamName.ShouldBe("blockers");
    }

    [Fact]
    public void WhenMoreBlockersAreNamedThanAnIssueRecords_Throws()
    {
        // Arrange
        TriageBlocker[] blockers = [.. Enumerable.Range(1, 51).Select(number => new TriageBlocker(number, "Needs it."))];

        // Act
        ArgumentException exception = Should.Throw<ArgumentException>(() => new TriageVerdict.Blocked(IssueKind.Feature, blockers, DecidedAt));

        // Assert
        exception.ParamName.ShouldBe("blockers");
    }

    [Fact]
    public void WhenExactlyTheMostBlockersAnIssueRecordsAreNamed_ExposesAllOfThem()
    {
        // Arrange
        TriageBlocker[] blockers = [.. Enumerable.Range(1, 50).Select(number => new TriageBlocker(number, "Needs it."))];

        // Act
        TriageVerdict.Blocked verdict = new(IssueKind.Feature, blockers, DecidedAt);

        // Assert
        verdict.BlockedBy.Count.ShouldBe(50);
    }

    [Fact]
    public void WhenAReasonIsTooLong_Throws()
    {
        // Arrange
        TriageBlocker[] blockers = [new TriageBlocker(15, new string('a', 501))];

        // Act
        ArgumentException exception = Should.Throw<ArgumentException>(() => new TriageVerdict.Blocked(IssueKind.Feature, blockers, DecidedAt));

        // Assert
        exception.ParamName.ShouldBe("blockers");
    }

    [Fact]
    public void WhenAReasonIsExactlyTheLongestAllowed_ExposesIt()
    {
        // Arrange
        string reason = new('a', 500);

        // Act
        TriageVerdict.Blocked verdict = new(IssueKind.Feature, [new TriageBlocker(15, reason)], DecidedAt);

        // Assert
        verdict.ReasonFor(15).ShouldBe(reason);
    }

    [Fact]
    public void WhenTheSameIssueIsNamedTwice_Throws()
    {
        // Arrange
        TriageBlocker[] blockers = [new TriageBlocker(15, "Needs the retry queue."), new TriageBlocker(15, "Needs it again.")];

        // Act
        ArgumentException exception = Should.Throw<ArgumentException>(() => new TriageVerdict.Blocked(IssueKind.Feature, blockers, DecidedAt));

        // Assert
        exception.ParamName.ShouldBe("blockers");
    }
}
