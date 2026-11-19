using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Infrastructure.Configurations;
using Foundry.Modules.Workers.Contracts;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Infrastructure.RevisionTriggerJsonConversionTests;

public sealed class ReviewFindings
{
    private static string Serialize(RevisionTrigger trigger) =>
        (string)RevisionTriggerJsonConversion.Converter.ConvertToProvider(trigger)!;

    private static RevisionTrigger Deserialize(string json) =>
        (RevisionTrigger)RevisionTriggerJsonConversion.Converter.ConvertFromProvider(json)!;

    private static RevisionTrigger.ReviewFindings Sample() =>
        new(
            [
                new ReviewFinding(
                    "src/a.cs",
                    12,
                    ReviewSeverity.Blocking,
                    ReviewCategory.Design,
                    new ReviewRule("design.1", null, null),
                    "The state allows an empty id."),
                new ReviewFinding("src/b.cs", null, ReviewSeverity.Advisory, ReviewCategory.Quality, null, "Consider a name."),
            ],
            [new ReviewUnmetCriterion("AC2", "Retries on 5xx.", "No retry path in the handler.")],
            ReviewTestsAssessment.Missing,
            "No test covers the retry.");

    [Fact]
    public void AReviewFindingsTrigger_RoundTripsItsFindingsCriteriaAndTestsAssessment()
    {
        // Arrange
        RevisionTrigger.ReviewFindings trigger = Sample();

        // Act
        RevisionTrigger read = Deserialize(Serialize(trigger));

        // Assert
        RevisionTrigger.ReviewFindings findings = read.ShouldBeOfType<RevisionTrigger.ReviewFindings>();
        findings.ShouldSatisfyAllConditions(
            () => findings.Findings.ShouldBe(trigger.Findings),
            () => findings.UnmetCriteria.ShouldBe(trigger.UnmetCriteria),
            () => findings.TestsAssessment.ShouldBe(ReviewTestsAssessment.Missing),
            () => findings.TestsReason.ShouldBe("No test covers the retry."));
    }

    [Fact]
    public void AReviewFindingsTrigger_IsWrittenWithItsDiscriminatorAndEnumNames()
    {
        // Act
        string json = Serialize(Sample());

        // Assert
        json.ShouldSatisfyAllConditions(
            () => json.ShouldContain("\"discriminator\":\"review_findings\""),
            () => json.ShouldContain("\"severity\":\"Blocking\""),
            () => json.ShouldContain("\"testsAssessment\":\"Missing\""));
    }

    [Fact]
    public void TwoEqualReviewFindingsTriggers_CompareEqual()
    {
        // Act
        bool equal = RevisionTriggerJsonConversion.Comparer.Equals(Sample(), Sample());

        // Assert
        equal.ShouldBeTrue();
    }

    [Fact]
    public void AReviewFeedbackTrigger_IsWrittenWithoutReviewFindingsProperties()
    {
        // Act
        string json = Serialize(new RevisionTrigger.ReviewFeedback([], 0, null));

        // Assert
        json.ShouldNotContain("findings");
    }
}
