using System.Text.Json.Nodes;

using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Verdicts;
using Foundry.UnitTests.Modules.Workers.Domain.ValueObjects.ReviewFactsTests;
using Foundry.UnitTests.Modules.Workers.Features.Verdicts.ReviewVerdictParserTests;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Domain.ValueObjects.ReviewVerdictTests;

public sealed class OutcomeAgainst
{
    private static ReviewFacts Facts(AcceptanceCriteria? criteria = null) =>
        ReviewFactsBuilder.With(
            [new ChangedPath(ChangeStatus.Modified, "src/App/Retry.cs"), new ChangedPath(ChangeStatus.Modified, "tests/RetryTests.cs")],
            criteria ?? AcceptanceCriteria.Extract("## Acceptance Criteria\n\n- one\n- two\n"));

    private static ReviewVerdictOutcome Outcome(JsonObject output, ReviewFacts? facts = null) =>
        ReviewVerdictParser.TryParse(output).ShouldNotBeNull().OutcomeAgainst(facts ?? Facts());

    [Fact]
    public void WhenEveryCriterionIsMetAndNothingBlocks_IsApproved()
    {
        // Act / Assert
        Outcome(ReviewVerdictSamples.Valid("AC1", "AC2")
            .WithFinding(ReviewVerdictSamples.Finding("quality", "advisory")))
            .ShouldBe(ReviewVerdictOutcome.Approved);
    }

    [Fact]
    public void WhenAFindingBlocks_RequestsChanges()
    {
        // Act / Assert
        Outcome(ReviewVerdictSamples.Valid("AC1", "AC2").WithFinding(ReviewVerdictSamples.Finding("correctness", "blocking")))
            .ShouldBe(ReviewVerdictOutcome.ChangesRequested);
    }

    [Fact]
    public void WhenTheTestsAreMissing_RequestsChanges()
    {
        // Act / Assert
        Outcome(ReviewVerdictSamples.Valid("AC1", "AC2").WithTests("missing")).ShouldBe(ReviewVerdictOutcome.ChangesRequested);
    }

    [Fact]
    public void WhenACriterionIsUnmet_RequestsChangesEvenWhenAnotherIsNotVerifiable()
    {
        // Act / Assert
        Outcome(ReviewVerdictSamples.Valid("AC1", "AC2").WithCriterion("AC1", "unmet").WithCriterion("AC2", "not_verifiable"))
            .ShouldBe(ReviewVerdictOutcome.ChangesRequested);
    }

    [Fact]
    public void WhenACriterionIsNotVerifiable_ApprovesForAPerson()
    {
        // Act / Assert
        Outcome(ReviewVerdictSamples.Valid("AC1", "AC2").WithCriterion("AC2", "not_verifiable"))
            .ShouldBe(ReviewVerdictOutcome.ApprovedForPerson);
    }

    [Fact]
    public void WhenNoCriteriaWereSupplied_ApprovesForAPerson()
    {
        // Act / Assert
        Outcome(ReviewVerdictSamples.Valid(), Facts(AcceptanceCriteria.None)).ShouldBe(ReviewVerdictOutcome.ApprovedForPerson);
    }

    [Fact]
    public void WhenTheTestsAreNotNeeded_DoesNotRequestChanges()
    {
        // Act / Assert
        Outcome(ReviewVerdictSamples.Valid("AC1", "AC2").WithTests("not_needed")).ShouldBe(ReviewVerdictOutcome.Approved);
    }
}
