using System.Text.Json.Nodes;

using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Verdicts;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Verdicts.ReviewVerdictParserTests;

public sealed class TryParse
{
    [Fact]
    public void WhenTheVerdictMatchesTheSchema_ReadsEveryPart()
    {
        // Arrange
        JsonObject output = ReviewVerdictSamples.Valid("AC1")
            .WithFinding(ReviewVerdictSamples.Finding("design", "blocking", new JsonObject { ["principle"] = "design.1" }));

        // Act
        ReviewVerdict verdict = ReviewVerdictParser.TryParse(output).ShouldNotBeNull();

        // Assert
        verdict.ShouldSatisfyAllConditions(
            () => verdict.Summary.ShouldBe("Adds retry to webhook delivery."),
            () => verdict.Findings.ShouldBe([new ReviewFinding(
                "src/App/Retry.cs",
                12,
                ReviewFindingSeverity.Blocking,
                ReviewCategory.Design,
                new ReviewRule("design.1", null, null),
                "The retry never stops.")]),
            () => verdict.Dimensions.Single(d => d.Category == ReviewCategory.Design).Status.ShouldBe(ReviewDimensionStatus.Findings),
            () => verdict.Dimensions.Count.ShouldBe(7),
            () => verdict.Criteria.ShouldBe([new ReviewCriterionAnswer("AC1", ReviewCriterionStatus.Met, "Covered by RetryTests.")]),
            () => verdict.Tests.ShouldBe(new ReviewTests(ReviewTestsAssessment.Adequate, "Retry is tested.", ["tests/App.Tests/RetryTests.cs"])));
    }

    [Fact]
    public void ToJson_WritesAVerdictTryParseReadsBackUnchanged()
    {
        // Arrange
        ReviewVerdict verdict = ReviewVerdictParser.TryParse(ReviewVerdictSamples.Valid("AC1", "AC2")
            .WithFinding(ReviewVerdictSamples.Finding("conventions", "advisory", new JsonObject { ["file"] = "CLAUDE.md", ["quote"] = "No var" })))
            .ShouldNotBeNull();

        // Act
        ReviewVerdict? reread = ReviewVerdictParser.TryParse(ReviewVerdictParser.ToJson(verdict));

        // Assert
        reread.ShouldBe(verdict);
    }

    public static TheoryData<string> SchemaBreaks() =>
    [
        "outcome",
        "missing-summary",
        "empty-summary",
        "unknown-finding-property",
        "bad-severity",
        "bad-category",
        "zero-line",
        "missing-dimension",
        "bad-dimension-status",
        "bad-criterion-id",
        "bad-assessment",
        "unknown-rule-property",
    ];

    [Theory]
    [MemberData(nameof(SchemaBreaks))]
    public void WhenTheVerdictBreaksTheSchema_ReturnsNull(string breakage)
    {
        // Arrange
        JsonObject output = ReviewVerdictSamples.Valid("AC1").WithFinding(ReviewVerdictSamples.Finding("correctness", "advisory"));
        JsonObject finding = output["findings"]![0]!.AsObject();

        switch (breakage)
        {
            case "outcome": output["outcome"] = "approved"; break;
            case "missing-summary": output.Remove("summary"); break;
            case "empty-summary": output["summary"] = string.Empty; break;
            case "unknown-finding-property": finding["extra"] = 1; break;
            case "bad-severity": finding["severity"] = "critical"; break;
            case "bad-category": finding["category"] = "style"; break;
            case "zero-line": finding["line"] = 0; break;
            case "missing-dimension": output["dimensions"]!.AsObject().Remove("security"); break;
            case "bad-dimension-status": output["dimensions"]!["security"]!["status"] = "fine"; break;
            case "bad-criterion-id": output["criteria"]![0]!["id"] = "criterion-1"; break;
            case "bad-assessment": output["tests"]!["assessment"] = "partial"; break;
            case "unknown-rule-property": finding["rule"] = new JsonObject { ["url"] = "https://example.com" }; break;
            default: throw new ArgumentOutOfRangeException(nameof(breakage), breakage, null);
        }

        // Act
        ReviewVerdict? verdict = ReviewVerdictParser.TryParse(output);

        // Assert
        verdict.ShouldBeNull();
    }

    [Theory]
    [InlineData(true, RunVerdictStatus.Done)]
    [InlineData(false, RunVerdictStatus.Invalid)]
    public void Parse_RecordsASchemaValidVerdictAsDone(bool valid, RunVerdictStatus expected)
    {
        // Arrange
        JsonObject output = ReviewVerdictSamples.Valid();

        if (!valid)
        {
            output.Remove("tests");
        }

        // Act
        RunVerdict verdict = new ReviewVerdictParser().Parse(output);

        // Assert
        verdict.Status.ShouldBe(expected);
    }
}
