using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Workers.Contracts;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.ValueObjects.TriageVerdictTests;

public sealed class Ready
{
    private static readonly DateTimeOffset DecidedAt = new(2026, 11, 26, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void WhenNoDemandIsGiven_ItIsDemandingWithNoReason()
    {
        // Act
        TriageVerdict.Ready verdict = new(IssueKind.Feature, DecidedAt);

        // Assert
        verdict.ShouldSatisfyAllConditions(
            () => verdict.Demand.ShouldBe(ImplementDemand.Demanding),
            () => verdict.DemandReason.ShouldBeNull());
    }

    [Fact]
    public void WhenARoutineDemandIsGiven_ItCarriesTheDemandAndReason()
    {
        // Act
        TriageVerdict.Ready verdict = new(IssueKind.Feature, DecidedAt, ImplementDemand.Routine, "none");

        // Assert
        verdict.ShouldSatisfyAllConditions(
            () => verdict.Demand.ShouldBe(ImplementDemand.Routine),
            () => verdict.DemandReason.ShouldBe("none"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void WhenTheReasonIsBlank_ItStoresNoReason(string reason)
    {
        // Act
        TriageVerdict.Ready verdict = new(IssueKind.Feature, DecidedAt, ImplementDemand.Routine, reason);

        // Assert
        verdict.DemandReason.ShouldBeNull();
    }

    [Fact]
    public void WhenTheReasonIsExactlyAtTheCap_ItIsKept()
    {
        // Arrange
        string reason = new('a', TriageVerdict.Ready.DemandReasonMaxLength);

        // Act
        TriageVerdict.Ready verdict = new(IssueKind.Feature, DecidedAt, ImplementDemand.Routine, reason);

        // Assert
        verdict.DemandReason.ShouldBe(reason);
    }

    [Fact]
    public void WhenTheReasonIsOverTheCap_Throws()
    {
        // Arrange
        string reason = new('a', TriageVerdict.Ready.DemandReasonMaxLength + 1);

        // Act
        ArgumentException exception = Should.Throw<ArgumentException>(
            () => new TriageVerdict.Ready(IssueKind.Feature, DecidedAt, ImplementDemand.Routine, reason));

        // Assert
        exception.ParamName.ShouldBe("demandReason");
    }

    [Fact]
    public void WhenTwoVerdictsDifferOnlyInDemand_TheyAreNotEqual()
    {
        // Arrange
        TriageVerdict.Ready routine = new(IssueKind.Feature, DecidedAt, ImplementDemand.Routine);

        // Act
        bool equal = routine == new TriageVerdict.Ready(IssueKind.Feature, DecidedAt, ImplementDemand.Demanding);

        // Assert
        equal.ShouldBeFalse();
    }
}
