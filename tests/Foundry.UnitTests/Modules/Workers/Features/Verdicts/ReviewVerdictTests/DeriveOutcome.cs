using System.Text.Json.Nodes;

using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Verdicts;

using Shouldly;

using Xunit;

using static Foundry.UnitTests.Modules.Workers.Features.Verdicts.ReviewVerdictTests.ReviewVerdictSamples;

namespace Foundry.UnitTests.Modules.Workers.Features.Verdicts.ReviewVerdictTests;

public sealed class DeriveOutcome
{
    private static ReviewVerdict.Valid Accept(JsonObject output, SuppliedReviewFacts facts) =>
        ReviewVerdict.TryParse(output).ShouldNotBeNull()
            .CheckAgainst(facts, PrincipleIds)
            .ShouldBeOfType<ReviewVerdictCheck.Accepted>()
            .Verdict;

    [Fact]
    public void WhenAFindingIsBlocking_RequestsChanges()
    {
        // Act
        ReviewVerdict.Valid valid = Accept(
            Verdict(
                findings: [Finding("security"), Finding("correctness", severity: "advisory")],
                dimensionStatuses: new() { ["security"] = "findings" }),
            Facts());

        // Assert
        valid.ShouldSatisfyAllConditions(
            () => valid.Outcome.ShouldBe(ReviewVerdictOutcome.ChangesRequested),
            () => valid.BlockingFindings.ShouldHaveSingleItem().Category.ShouldBe(ReviewCategory.Security));
    }

    [Fact]
    public void WhenTestsAreMissing_RequestsChanges()
    {
        // Act / Assert
        Accept(Verdict(assessment: "missing"), Facts()).Outcome.ShouldBe(ReviewVerdictOutcome.ChangesRequested);
    }

    [Fact]
    public void WhenACriterionIsUnmet_RequestsChangesAndNamesIt()
    {
        // Act
        ReviewVerdict.Valid valid = Accept(
            Verdict(criteria: [Criterion("AC1", "met"), Criterion("AC2", "unmet")]),
            Facts(criteriaCount: 2));

        // Assert
        valid.ShouldSatisfyAllConditions(
            () => valid.Outcome.ShouldBe(ReviewVerdictOutcome.ChangesRequested),
            () => valid.UnmetCriteria.ShouldBe(["AC2"]));
    }

    [Fact]
    public void WhenACriterionIsNotVerifiableAndNothingBlocks_ApprovesForAPerson()
    {
        // Act / Assert
        Accept(Verdict(criteria: [Criterion("AC1", "not_verifiable")]), Facts())
            .Outcome.ShouldBe(ReviewVerdictOutcome.ApprovedForPerson);
    }

    [Fact]
    public void WhenNoCriteriaWereSuppliedAndNothingBlocks_ApprovesForAPerson()
    {
        // Act / Assert
        Accept(Verdict(criteria: []), Facts(criteriaCount: 0)).Outcome.ShouldBe(ReviewVerdictOutcome.ApprovedForPerson);
    }

    [Theory]
    [InlineData("adequate")]
    [InlineData("not_needed")]
    public void WhenNothingBlocksAndEveryCriterionIsMet_Approves(string assessment)
    {
        // Act / Assert
        Accept(
                Verdict(findings: [Finding("quality", severity: "advisory")], assessment: assessment),
                Facts())
            .Outcome.ShouldBe(ReviewVerdictOutcome.Approved);
    }

    [Fact]
    public void WhenChecksPassedAndEveryCriterionIsMetOrCoveredByChecks_Approves()
    {
        // Act
        ReviewVerdict.Valid valid = Accept(
            Verdict(criteria: [Criterion("AC1", "met"), Criterion("AC2", "covered_by_checks")]),
            Facts(criteriaCount: 2, checksPassed: true));

        // Assert
        valid.ShouldSatisfyAllConditions(
            () => valid.Outcome.ShouldBe(ReviewVerdictOutcome.Approved),
            () => valid.ToRecorded().Criteria.Select(answer => answer.Status)
                .ShouldBe([ReviewCriterionStatus.Met, ReviewCriterionStatus.CoveredByChecks]));
    }

    [Fact]
    public void WhenChecksPassedAndACriterionIsCoveredByChecksBesideABlockingFinding_RequestsChanges()
    {
        // Act / Assert
        Accept(
                Verdict(
                    findings: [Finding("correctness")],
                    dimensionStatuses: new() { ["correctness"] = "findings" },
                    criteria: [Criterion("AC1", "covered_by_checks")]),
                Facts(checksPassed: true))
            .Outcome.ShouldBe(ReviewVerdictOutcome.ChangesRequested);
    }

    [Fact]
    public void WhenChecksDidNotPassAndACriterionIsCoveredByChecks_RecordsItNotVerifiableWithWhy()
    {
        // Act
        ReviewVerdict.Valid valid = Accept(
            Verdict(criteria: [Criterion("AC1", "met"), Criterion("AC2", "covered_by_checks")]),
            Facts(criteriaCount: 2, checksPassed: false));

        // Assert
        valid.ToRecorded().Criteria.ShouldBe([
            new ReviewCriterionAnswer("AC1", ReviewCriterionStatus.Met, "See tests."),
            new ReviewCriterionAnswer("AC2", ReviewCriterionStatus.NotVerifiable, ReviewVerdict.CoveredByChecksUnavailableEvidence),
        ]);
    }

    [Fact]
    public void WhenChecksDidNotPassAndACriterionIsCoveredByChecksAndNothingElseBlocks_ApprovesForAPerson()
    {
        // Act / Assert
        Accept(Verdict(criteria: [Criterion("AC1", "covered_by_checks")]), Facts(checksPassed: false))
            .Outcome.ShouldBe(ReviewVerdictOutcome.ApprovedForPerson);
    }

    [Fact]
    public void WhenChecksDidNotPassAndACriterionIsCoveredByChecksBesideABlockingFinding_RequestsChanges()
    {
        // Act / Assert
        Accept(
                Verdict(
                    findings: [Finding("security")],
                    dimensionStatuses: new() { ["security"] = "findings" },
                    criteria: [Criterion("AC1", "covered_by_checks")]),
                Facts(checksPassed: false))
            .Outcome.ShouldBe(ReviewVerdictOutcome.ChangesRequested);
    }
}
