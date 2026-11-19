using System.Text.Json.Nodes;

using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Features.Verdicts;

using Shouldly;

using Xunit;

using static Foundry.UnitTests.Modules.Workers.Features.Verdicts.ReviewVerdictTests.ReviewVerdictSamples;

namespace Foundry.UnitTests.Modules.Workers.Features.Verdicts.ReviewVerdictTests;

public sealed class TryParse
{
    [Fact]
    public void WhenTheVerdictMatchesTheSchema_ReadsEveryPart()
    {
        // Arrange
        JsonObject output = Verdict(
            findings: [Finding("design", rule: new JsonObject { ["principle"] = "design.1" })],
            dimensionStatuses: new() { ["design"] = "findings", ["security"] = "not_applicable" },
            criteria: [Criterion("AC1", "not_verifiable")],
            assessment: "not_needed");

        // Act
        ReviewVerdict verdict = ReviewVerdict.TryParse(output).ShouldNotBeNull();

        // Assert
        verdict.ShouldSatisfyAllConditions(
            () => verdict.Summary.ShouldBe("Adds the thing."),
            () => verdict.Findings.ShouldBe([new ReviewFinding(
                "src/a.cs",
                3,
                ReviewSeverity.Blocking,
                ReviewCategory.Design,
                new ReviewRule("design.1", null, null),
                "Wrong.")]),
            () => verdict.Dimensions.Select(dimension => dimension.Category).ShouldBe(Enum.GetValues<ReviewCategory>()),
            () => verdict.Dimensions.Single(dimension => dimension.Category == ReviewCategory.Design).Status
                .ShouldBe(ReviewDimensionStatus.Findings),
            () => verdict.Dimensions.Single(dimension => dimension.Category == ReviewCategory.Security).Status
                .ShouldBe(ReviewDimensionStatus.NotApplicable),
            () => verdict.Criteria.ShouldBe([new ReviewCriterionAnswer("AC1", ReviewCriterionStatus.NotVerifiable, "See tests.")]),
            () => verdict.Tests.Assessment.ShouldBe(ReviewTestsAssessment.NotNeeded),
            () => verdict.Tests.CoveringTests.ShouldBe(["tests/a_test.go"]));
    }

    [Fact]
    public void WhenAFindingHasNoLineOrRule_ReadsThemAsAbsent()
    {
        // Arrange
        JsonObject finding = Finding("correctness", severity: "advisory");
        finding.Remove("line");

        // Act
        ReviewVerdict verdict = ReviewVerdict.TryParse(Verdict(findings: [finding])).ShouldNotBeNull();

        // Assert
        verdict.Findings.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            parsed => parsed.Line.ShouldBeNull(),
            parsed => parsed.Rule.ShouldBeNull());
    }

    public static TheoryData<string> Breakages =>
    [
        "outcome",
        "missing-summary",
        "empty-summary",
        "unknown-severity",
        "unknown-category",
        "zero-line",
        "extra-finding-property",
        "extra-rule-property",
        "missing-dimension",
        "unknown-dimension-status",
        "bad-criterion-id",
        "unknown-criterion-status",
        "unknown-assessment",
        "non-string-covering-test",
    ];

    [Theory]
    [MemberData(nameof(Breakages))]
    public void WhenTheVerdictBreaksTheSchemaShape_ReturnsNull(string breakage)
    {
        // Arrange
        JsonObject output = Verdict(findings: [Finding("correctness")]);
        JsonObject finding = output["findings"]![0]!.AsObject();

        switch (breakage)
        {
            case "outcome": output["outcome"] = "approved"; break;
            case "missing-summary": output.Remove("summary"); break;
            case "empty-summary": output["summary"] = string.Empty; break;
            case "unknown-severity": finding["severity"] = "major"; break;
            case "unknown-category": finding["category"] = "style"; break;
            case "zero-line": finding["line"] = 0; break;
            case "extra-finding-property": finding["fix"] = "Do it."; break;
            case "extra-rule-property": finding["rule"] = new JsonObject { ["section"] = "design" }; break;
            case "missing-dimension": output["dimensions"]!.AsObject().Remove("tests"); break;
            case "unknown-dimension-status": output["dimensions"]!["tests"]!["status"] = "ok"; break;
            case "bad-criterion-id": output["criteria"]![0]!["id"] = "AC1\n"; break;
            case "unknown-criterion-status": output["criteria"]![0]!["status"] = "partial"; break;
            case "unknown-assessment": output["tests"]!["assessment"] = "partial"; break;
            case "non-string-covering-test": output["tests"]!["coveringTests"] = new JsonArray(1); break;
            default: throw new ArgumentOutOfRangeException(nameof(breakage), breakage, null);
        }

        // Act / Assert
        ReviewVerdict.TryParse(output).ShouldBeNull();
    }
}
