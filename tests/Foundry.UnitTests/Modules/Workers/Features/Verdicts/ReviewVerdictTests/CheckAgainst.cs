using System.Text.Json.Nodes;

using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Verdicts;

using Shouldly;

using Xunit;

using static Foundry.UnitTests.Modules.Workers.Features.Verdicts.ReviewVerdictTests.ReviewVerdictSamples;

namespace Foundry.UnitTests.Modules.Workers.Features.Verdicts.ReviewVerdictTests;

public sealed class CheckAgainst
{
    private static ReviewVerdictCheck Check(JsonObject output, SuppliedReviewFacts? facts = null) =>
        ReviewVerdict.TryParse(output).ShouldNotBeNull().CheckAgainst(facts ?? Facts(), PrincipleIds);

    private static string RejectionOf(JsonObject output, SuppliedReviewFacts? facts = null) =>
        Check(output, facts).ShouldBeOfType<ReviewVerdictCheck.Rejected>().Reason.Detail;

    private static ReviewVerdict.Valid AcceptedOf(JsonObject output, SuppliedReviewFacts? facts = null) =>
        Check(output, facts).ShouldBeOfType<ReviewVerdictCheck.Accepted>().Verdict;

    [Fact]
    public void WhenADimensionIsFindingsWithNoFindingInIt_Rejects()
    {
        // Act / Assert
        RejectionOf(Verdict(dimensionStatuses: new() { ["security"] = "findings" }))
            .ShouldContain("'security' dimension as findings");
    }

    [Theory]
    [InlineData("clear")]
    [InlineData("not_applicable")]
    public void WhenADimensionIsClearOrNotApplicableBesideABlockingFindingInIt_Rejects(string status)
    {
        // Act / Assert
        RejectionOf(Verdict(findings: [Finding("correctness")], dimensionStatuses: new() { ["correctness"] = status }))
            .ShouldContain("'correctness' dimension as " + status);
    }

    [Fact]
    public void WhenADimensionIsClearBesideOnlyAnAdvisoryFindingInIt_Accepts()
    {
        // Act / Assert
        AcceptedOf(Verdict(findings: [Finding("correctness", severity: "advisory")]))
            .Outcome.ShouldBe(ReviewVerdictOutcome.Approved);
    }

    [Theory]
    [InlineData("quality")]
    [InlineData("design")]
    [InlineData("conventions")]
    public void WhenABlockingRuleBackedFindingCitesNoRule_Rejects(string category)
    {
        // Act / Assert
        RejectionOf(Verdict(findings: [Finding(category)], dimensionStatuses: new() { [category] = "findings" }))
            .ShouldContain("cites no rule");
    }

    [Fact]
    public void WhenABlockingRuleBackedFindingCitesBothRuleForms_Rejects()
    {
        // Arrange
        JsonObject rule = new() { ["principle"] = "design.1", ["file"] = "CLAUDE.md", ["quote"] = "No var." };

        // Act / Assert
        RejectionOf(Verdict(findings: [Finding("design", rule: rule)], dimensionStatuses: new() { ["design"] = "findings" }))
            .ShouldContain("both a principle and a convention file");
    }

    [Fact]
    public void WhenABlockingCorrectnessFindingCitesNoRule_Accepts()
    {
        // Act / Assert
        AcceptedOf(Verdict(findings: [Finding("correctness")], dimensionStatuses: new() { ["correctness"] = "findings" }))
            .Outcome.ShouldBe(ReviewVerdictOutcome.ChangesRequested);
    }

    [Fact]
    public void WhenARuleCitesAPrincipleTheRoleDoesNotHave_Rejects()
    {
        // Arrange
        JsonObject rule = new() { ["principle"] = "design.99" };

        // Act / Assert
        RejectionOf(Verdict(findings: [Finding("design", rule: rule)], dimensionStatuses: new() { ["design"] = "findings" }))
            .ShouldContain("'design.99'");
    }

    [Theory]
    [InlineData("CLAUDE.md", "")]
    [InlineData("", "No var.")]
    [InlineData(null, "No var.")]
    public void WhenAConventionRuleHasAnEmptyFileOrQuote_Rejects(string? file, string quote)
    {
        // Arrange
        JsonObject rule = new() { ["quote"] = quote };

        if (file is not null)
        {
            rule["file"] = file;
        }

        // Act / Assert
        RejectionOf(Verdict(findings: [Finding("conventions", rule: rule)], dimensionStatuses: new() { ["conventions"] = "findings" }))
            .ShouldContain("without both a file and a quote");
    }

    [Fact]
    public void WhenABlockingConventionsFindingCitesAFileAndQuote_Accepts()
    {
        // Arrange
        JsonObject rule = new() { ["file"] = "CLAUDE.md", ["quote"] = "No var." };

        // Act / Assert
        Check(Verdict(findings: [Finding("conventions", rule: rule)], dimensionStatuses: new() { ["conventions"] = "findings" }))
            .ShouldBeOfType<ReviewVerdictCheck.Accepted>();
    }

    [Fact]
    public void WhenASuppliedCriterionIsNotAnswered_Rejects()
    {
        // Act / Assert
        RejectionOf(Verdict(criteria: [Criterion("AC1", "met")]), Facts(criteriaCount: 2)).ShouldContain("AC2");
    }

    [Fact]
    public void WhenACriterionIsAnsweredTwice_Rejects()
    {
        // Act / Assert
        RejectionOf(Verdict(criteria: [Criterion("AC1", "met"), Criterion("AC1", "unmet")])).ShouldContain("more than once");
    }

    [Fact]
    public void WhenAnUnsuppliedCriterionIsAnswered_Rejects()
    {
        // Act / Assert
        RejectionOf(Verdict(criteria: [Criterion("AC1", "met"), Criterion("AC2", "met")])).ShouldContain("AC2, which was not supplied");
    }

    [Fact]
    public void WhenNoCriteriaWereSuppliedAndNoneAreAnswered_Accepts()
    {
        // Act / Assert
        AcceptedOf(Verdict(criteria: []), Facts(criteriaCount: 0)).Outcome.ShouldBe(ReviewVerdictOutcome.ApprovedForPerson);
    }

    [Fact]
    public void WhenTheFlagIsSetAndAdequateTestsNameNoCoveringTest_Rejects()
    {
        // Act / Assert
        RejectionOf(Verdict(coveringTests: []), Facts(productionWithoutTests: true)).ShouldContain("names no covering test");
    }

    [Fact]
    public void WhenTheFlagIsSetAndAdequateTestsNameAPathThatIsNotATest_Rejects()
    {
        // Act / Assert
        RejectionOf(Verdict(coveringTests: new JsonArray("tests/a_test.go", "src/a.cs")), Facts(productionWithoutTests: true))
            .ShouldContain("'src/a.cs'");
    }

    [Fact]
    public void WhenTheFlagIsSetAndAdequateTestsNameOnlyTestPaths_Accepts()
    {
        // Act / Assert
        Check(Verdict(coveringTests: new JsonArray("tests/existing_test.go")), Facts(productionWithoutTests: true))
            .ShouldBeOfType<ReviewVerdictCheck.Accepted>();
    }

    [Fact]
    public void WhenTheFlagIsNotSet_DoesNotCheckTheCoveringTests()
    {
        // Act / Assert
        Check(Verdict(coveringTests: new JsonArray("src/a.cs")), Facts()).ShouldBeOfType<ReviewVerdictCheck.Accepted>();
    }

    [Fact]
    public void WhenTheFlagIsSetAndTestsAreMissing_AcceptsAndRequestsChanges()
    {
        // Act / Assert
        AcceptedOf(Verdict(assessment: "missing", coveringTests: []), Facts(productionWithoutTests: true))
            .Outcome.ShouldBe(ReviewVerdictOutcome.ChangesRequested);
    }

    [Fact]
    public void WhenAccepted_RecordsTheReviewedCommitPatchIdAndOutcome()
    {
        // Arrange
        SuppliedReviewFacts facts = Facts();

        // Act
        RecordedReviewVerdict recorded = AcceptedOf(Verdict(), facts).ToRecorded();

        // Assert
        recorded.ShouldSatisfyAllConditions(
            () => recorded.Outcome.ShouldBe(ReviewVerdictOutcome.Approved),
            () => recorded.ReviewedCommit.ShouldBe(facts.ReviewedCommit.Value),
            () => recorded.PatchId.ShouldBe(facts.PatchId!.Value.Value),
            () => recorded.Summary.ShouldBe("Adds the thing."));
    }
}
