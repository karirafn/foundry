using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Features.Verdicts;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Verdicts.TriageVerdictTests;

public sealed class BlockedEquality
{
    private static TriageVerdict.Blocked Of(params TriageBlocker[] blockers) => new(TriageIssueKind.Bug, blockers);

    [Fact]
    public void WhenTheBlockersDiffer_TheVerdictsAreNotEqualAndHashDifferently()
    {
        // Arrange
        TriageVerdict.Blocked first = Of(new TriageBlocker(15, "Needs it."));
        TriageVerdict.Blocked second = Of(new TriageBlocker(16, "Needs it."));

        // Act
        bool equal = first.Equals(second);

        // Assert
        first.ShouldSatisfyAllConditions(
            () => equal.ShouldBeFalse(),
            () => first.GetHashCode().ShouldNotBe(second.GetHashCode()));
    }

    [Fact]
    public void WhenTheSameBlockersComeInADifferentOrder_TheVerdictsAreNotEqualAndHashDifferently()
    {
        // Arrange
        TriageVerdict.Blocked first = Of(new TriageBlocker(15, "A."), new TriageBlocker(16, "B."));
        TriageVerdict.Blocked second = Of(new TriageBlocker(16, "B."), new TriageBlocker(15, "A."));

        // Act
        bool equal = first.Equals(second);

        // Assert
        first.ShouldSatisfyAllConditions(
            () => equal.ShouldBeFalse(),
            () => first.GetHashCode().ShouldNotBe(second.GetHashCode()));
    }

    [Fact]
    public void WhenTheBlockersMatchInOrder_TheVerdictsAreEqualAndHashAlike()
    {
        // Arrange
        TriageVerdict.Blocked first = Of(new TriageBlocker(15, "A."), new TriageBlocker(16, "B."));
        TriageVerdict.Blocked second = Of(new TriageBlocker(15, "A."), new TriageBlocker(16, "B."));

        // Act
        bool equal = first.Equals(second);

        // Assert
        first.ShouldSatisfyAllConditions(
            () => equal.ShouldBeTrue(),
            () => first.GetHashCode().ShouldBe(second.GetHashCode()));
    }
}
