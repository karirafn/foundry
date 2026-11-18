using System.Text.Json.Nodes;

using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Verdicts;
using Foundry.UnitTests.Modules.Workers.Domain.ValueObjects.ReviewFactsTests;
using Foundry.UnitTests.Modules.Workers.Features.Verdicts.ReviewVerdictParserTests;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Domain.ValueObjects.ReviewVerdictTests;

public sealed class RejectionAgainst
{
    private static readonly string[] PrincipleIds = ["design.1", "code.2"];

    private static readonly AcceptanceCriteria TwoCriteria =
        AcceptanceCriteria.Extract("## Acceptance Criteria\n\n- one\n- two\n");

    private static ReviewFacts Facts(string path = "tests/App.Tests/RetryTests.cs", AcceptanceCriteria? criteria = null) =>
        ReviewFactsBuilder.With(
            [new ChangedPath(ChangeStatus.Modified, "src/App/Retry.cs"), new ChangedPath(ChangeStatus.Modified, path)],
            criteria ?? TwoCriteria);

    private static FailureReason.VerdictInvalid? Reject(JsonObject output, ReviewFacts? facts = null) =>
        ReviewVerdictParser.TryParse(output).ShouldNotBeNull().RejectionAgainst(facts ?? Facts(), PrincipleIds);

    private static JsonObject Valid() => ReviewVerdictSamples.Valid("AC1", "AC2");

    [Fact]
    public void WhenTheVerdictAgreesWithTheFacts_AcceptsIt()
    {
        // Act / Assert
        Reject(Valid()
            .WithFinding(ReviewVerdictSamples.Finding("design", "blocking", new JsonObject { ["principle"] = "design.1" }))
            .WithFinding(ReviewVerdictSamples.Finding("conventions", "blocking", new JsonObject { ["file"] = "CLAUDE.md", ["quote"] = "No var" }))
            .WithFinding(ReviewVerdictSamples.Finding("quality", "advisory")))
            .ShouldBeNull();
    }

    [Fact]
    public void WhenADimensionHasFindingsButNoFindingInItsCategory_Rejects()
    {
        // Arrange
        JsonObject output = Valid();
        output["dimensions"]!["security"]!["status"] = "findings";

        // Act / Assert
        Reject(output).ShouldNotBeNull().Detail.ShouldContain("'security'");
    }

    [Theory]
    [InlineData("clear")]
    [InlineData("not_applicable")]
    public void WhenADimensionIsAnsweredWithoutFindingsWhileABlockingFindingHasItsCategory_Rejects(string status)
    {
        // Arrange
        JsonObject output = Valid().WithFinding(ReviewVerdictSamples.Finding("correctness", "blocking"));
        output["dimensions"]!["correctness"]!["status"] = status;

        // Act / Assert
        Reject(output).ShouldNotBeNull().Detail.ShouldContain("'correctness'");
    }

    [Fact]
    public void WhenADimensionIsClearWhileOnlyAnAdvisoryFindingHasItsCategory_Accepts()
    {
        // Arrange
        JsonObject output = Valid().WithFinding(ReviewVerdictSamples.Finding("correctness", "advisory"));
        output["dimensions"]!["correctness"]!["status"] = "clear";

        // Act / Assert
        Reject(output).ShouldBeNull();
    }

    [Theory]
    [InlineData("quality")]
    [InlineData("design")]
    [InlineData("conventions")]
    public void WhenABlockingRuleBackedFindingCitesNoRule_Rejects(string category)
    {
        // Act / Assert
        Reject(Valid().WithFinding(ReviewVerdictSamples.Finding(category, "blocking")))
            .ShouldNotBeNull().Detail.ShouldContain("cites no rule");
    }

    [Fact]
    public void WhenABlockingRuleBackedFindingHasAnEmptyRule_Rejects()
    {
        // Act / Assert
        Reject(Valid().WithFinding(ReviewVerdictSamples.Finding("design", "blocking", [])))
            .ShouldNotBeNull().Detail.ShouldContain("cites no rule");
    }

    [Fact]
    public void WhenABlockingCorrectnessFindingCitesNoRule_Accepts()
    {
        // Act / Assert
        Reject(Valid().WithFinding(ReviewVerdictSamples.Finding("correctness", "blocking"))).ShouldBeNull();
    }

    [Fact]
    public void WhenABlockingFindingCitesBothRuleForms_Rejects()
    {
        // Arrange
        JsonObject rule = new() { ["principle"] = "design.1", ["file"] = "CLAUDE.md", ["quote"] = "No var" };

        // Act / Assert
        Reject(Valid().WithFinding(ReviewVerdictSamples.Finding("design", "blocking", rule)))
            .ShouldNotBeNull().Detail.ShouldContain("both");
    }

    [Fact]
    public void WhenARuleCitesAPrincipleIdTheRoleDoesNotCarry_Rejects()
    {
        // Act / Assert
        Reject(Valid().WithFinding(ReviewVerdictSamples.Finding("design", "blocking", new JsonObject { ["principle"] = "design.99" })))
            .ShouldNotBeNull().Detail.ShouldContain("'design.99'");
    }

    [Theory]
    [InlineData("", "No var")]
    [InlineData("CLAUDE.md", "")]
    [InlineData("CLAUDE.md", null)]
    public void WhenAConventionRuleHasAnEmptyFileOrQuote_Rejects(string file, string? quote)
    {
        // Arrange
        JsonObject rule = new() { ["file"] = file };

        if (quote is not null)
        {
            rule["quote"] = quote;
        }

        // Act / Assert
        Reject(Valid().WithFinding(ReviewVerdictSamples.Finding("conventions", "blocking", rule)))
            .ShouldNotBeNull().Detail.ShouldContain("file and a quote");
    }

    [Theory]
    [InlineData("AC1")]
    [InlineData("AC1,AC2,AC2")]
    [InlineData("AC1,AC2,AC3")]
    [InlineData("")]
    public void WhenTheCriteriaIdsAreNotExactlyTheSuppliedOnes_Rejects(string ids)
    {
        // Act / Assert
        Reject(ReviewVerdictSamples.Valid(ids.Split(',', StringSplitOptions.RemoveEmptyEntries))).ShouldNotBeNull().Detail.ShouldContain("criteria");
    }

    [Fact]
    public void WhenNoCriteriaWereSuppliedAndNoneAreAnswered_Accepts()
    {
        // Act / Assert
        Reject(ReviewVerdictSamples.Valid(), Facts(criteria: AcceptanceCriteria.None)).ShouldBeNull();
    }

    [Fact]
    public void WhenTheFlagIsSetAndAdequateTestsNameNoCoveringTest_Rejects()
    {
        // Act / Assert
        Reject(Valid().WithTests("adequate"), Facts(path: "docs/guide.md"))
            .ShouldNotBeNull().Detail.ShouldContain("no covering test");
    }

    [Fact]
    public void WhenTheFlagIsSetAndAdequateTestsNameANonTestPath_Rejects()
    {
        // Act / Assert
        Reject(Valid().WithTests("adequate", "tests/App.Tests/RetryTests.cs", "src/App/Retry.cs"), Facts(path: "docs/guide.md"))
            .ShouldNotBeNull().Detail.ShouldContain("'src/App/Retry.cs'");
    }

    [Fact]
    public void WhenTheFlagIsSetAndAdequateTestsNameTestPaths_Accepts()
    {
        // Act / Assert
        Reject(Valid().WithTests("adequate", "tests/App.Tests/RetryTests.cs"), Facts(path: "docs/guide.md")).ShouldBeNull();
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("not_needed")]
    public void WhenTheFlagIsSetAndTheTestsAreNotJudgedAdequate_AcceptsWithoutCoveringTests(string assessment)
    {
        // Act / Assert
        Reject(Valid().WithTests(assessment), Facts(path: "docs/guide.md")).ShouldBeNull();
    }

    [Fact]
    public void WhenTheFlagIsNotSet_AdequateTestsNeedNoCoveringTest()
    {
        // Act / Assert
        Reject(Valid().WithTests("adequate")).ShouldBeNull();
    }
}
