using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Features.Reviews;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Reviews.ReviewVerdictCommentTests;

public sealed class Compose
{
    private const string ReviewedCommit = "0123456789abcdef0123456789abcdef01234567";
    private const int ProviderSafeLength = 60_000;

    private static ReviewFinding Blocking(string body, string path = "src/Retry.cs", int? line = 12, ReviewRule? rule = null) =>
        new(path, line, ReviewSeverity.Blocking, ReviewCategory.Correctness, rule, body);

    private static ReviewFinding Advisory(string body, string path = "src/Retry.cs", int? line = 30, ReviewRule? rule = null) =>
        new(path, line, ReviewSeverity.Advisory, ReviewCategory.Quality, rule, body);

    private static RecordedReviewVerdict Verdict(
        ReviewVerdictOutcome outcome = ReviewVerdictOutcome.Approved,
        IReadOnlyList<ReviewFinding>? findings = null,
        IReadOnlyList<ReviewCriterionAnswer>? criteria = null,
        ReviewTests? tests = null,
        string summary = "Adds retry to webhook delivery.") =>
        new(
            summary,
            findings ?? [],
            [],
            criteria ?? [],
            tests ?? new ReviewTests(ReviewTestsAssessment.Adequate, "RetryTests covers the new path.", []),
            outcome,
            ReviewedCommit,
            PatchId: null);

    [Theory]
    [InlineData(nameof(ReviewVerdictOutcome.Approved), "> [!TIP]", "> **Approved**")]
    [InlineData(nameof(ReviewVerdictOutcome.ApprovedForPerson), "> [!WARNING]", "> **Approved, a person should check**")]
    [InlineData(nameof(ReviewVerdictOutcome.ChangesRequested), "> [!CAUTION]", "> **Changes requested**")]
    public void EachOutcome_LeadsWithItsAlertTagAloneOnTheFirstLine(string outcomeName, string tagLine, string headlineStart)
    {
        // Act
        string comment = ReviewVerdictComment.Compose(Verdict(Enum.Parse<ReviewVerdictOutcome>(outcomeName)));

        // Assert
        string[] lines = comment.Split('\n');
        comment.ShouldSatisfyAllConditions(
            () => lines[0].ShouldBe(tagLine),
            () => lines[1].ShouldStartWith(headlineStart),
            () => lines[2].ShouldBe("> Adds retry to webhook delivery."),
            () => comment.Split("[!").Length.ShouldBe(2));
    }

    [Fact]
    public void AVerdictWithNoFindings_SaysNoFindingsInPlaceOfTheCountsAndHasNoFindingLists()
    {
        // Act
        string comment = ReviewVerdictComment.Compose(Verdict());

        // Assert
        comment.ShouldSatisfyAllConditions(
            () => comment.Split('\n')[1].ShouldBe("> **Approved**: No findings"),
            () => comment.ShouldNotContain("**Blocking**"),
            () => comment.ShouldNotContain("Advisory findings"),
            () => comment.ShouldNotContain("**Criteria not met**"));
    }

    [Fact]
    public void AVerdictWithFindings_CountsBlockingAndAdvisoryInTheHeadline()
    {
        // Act
        string comment = ReviewVerdictComment.Compose(Verdict(
            ReviewVerdictOutcome.ChangesRequested,
            [Blocking("first"), Blocking("second"), Advisory("third"), Advisory("fourth"), Advisory("fifth")]));

        // Assert
        comment.Split('\n')[1].ShouldBe("> **Changes requested**: 2 blocking, 3 advisory");
    }

    [Fact]
    public void BlockingFindings_AreListedInViewWithTheirLocationCategoryAndPrinciple()
    {
        // Act
        string comment = ReviewVerdictComment.Compose(Verdict(
            ReviewVerdictOutcome.ChangesRequested,
            [Blocking("Retries forever on a 4xx.", rule: new ReviewRule("design.4", null, null))]));

        // Assert
        comment.ShouldContain("**Blocking**\n- `src/Retry.cs:12`: Retries forever on a 4xx. (correctness · design.4)\n");
    }

    [Fact]
    public void AFindingCitingAConventionFile_ShowsTheFileAndNotTheQuote()
    {
        // Act
        string comment = ReviewVerdictComment.Compose(Verdict(
            ReviewVerdictOutcome.ChangesRequested,
            [Blocking("Uses var.", rule: new ReviewRule(null, "CLAUDE.md", "No var, explicit types enforced"))]));

        // Assert
        comment.ShouldSatisfyAllConditions(
            () => comment.ShouldContain("- `src/Retry.cs:12`: Uses var. (correctness · CLAUDE.md)\n"),
            () => comment.ShouldNotContain("explicit types enforced"));
    }

    [Fact]
    public void AFindingWithoutARuleOrLine_ShowsOnlyItsPathAndCategory()
    {
        // Act
        string comment = ReviewVerdictComment.Compose(Verdict(
            ReviewVerdictOutcome.ChangesRequested,
            [Blocking("Missing null check.", line: null)]));

        // Assert
        comment.ShouldContain("- `src/Retry.cs`: Missing null check. (correctness)\n");
    }

    [Fact]
    public void OnlyAdvisoryFindings_AreCollapsedAndNoBlockingListIsShown()
    {
        // Act
        string comment = ReviewVerdictComment.Compose(Verdict(findings: [Advisory("Name could be clearer.")]));

        // Assert
        comment.ShouldSatisfyAllConditions(
            () => comment.Split('\n')[1].ShouldBe("> **Approved**: 0 blocking, 1 advisory"),
            () => comment.ShouldNotContain("**Blocking**"),
            () => comment.ShouldContain(
                "<details><summary>Advisory findings (1)</summary>\n\n- `src/Retry.cs:30`: Name could be clearer. (quality)\n\n</details>"));
    }

    [Fact]
    public void UnmetAndUnverifiableCriteria_AreListedInViewAndEveryCriterionsEvidenceIsCollapsed()
    {
        // Act
        string comment = ReviewVerdictComment.Compose(Verdict(
            ReviewVerdictOutcome.ChangesRequested,
            criteria:
            [
                new ReviewCriterionAnswer("AC1", ReviewCriterionStatus.Met, "RetryTests.cs covers it."),
                new ReviewCriterionAnswer("AC2", ReviewCriterionStatus.Unmet, "No backoff."),
                new ReviewCriterionAnswer("AC3", ReviewCriterionStatus.NotVerifiable, "Needs a live provider."),
            ]));

        // Assert
        comment.ShouldSatisfyAllConditions(
            () => comment.ShouldContain("**Criteria not met**\n- AC2: unmet\n- AC3: not verifiable\n"),
            () => comment.ShouldContain(
                "<details><summary>Criteria evidence</summary>\n\n- AC1 (met): RetryTests.cs covers it.\n- AC2 (unmet): No backoff.\n- AC3 (not verifiable): Needs a live provider.\n\n</details>"));
    }

    [Fact]
    public void EveryCriterionMet_OmitsTheCriteriaNotMetList()
    {
        // Act
        string comment = ReviewVerdictComment.Compose(Verdict(
            criteria: [new ReviewCriterionAnswer("AC1", ReviewCriterionStatus.Met, "Covered.")]));

        // Assert
        comment.ShouldNotContain("**Criteria not met**");
    }

    [Fact]
    public void ACriterionCoveredByChecks_IsNotListedAsNotMetAndShowsAsCoveredByChecksInTheEvidence()
    {
        // Act
        string comment = ReviewVerdictComment.Compose(Verdict(
            criteria:
            [
                new ReviewCriterionAnswer("AC1", ReviewCriterionStatus.Met, "RetryTests.cs covers it."),
                new ReviewCriterionAnswer("AC2", ReviewCriterionStatus.CoveredByChecks, "The checks gate passed."),
            ]));

        // Assert
        comment.ShouldSatisfyAllConditions(
            () => comment.ShouldNotContain("**Criteria not met**"),
            () => comment.ShouldContain("- AC2 (covered by checks): The checks gate passed.\n"));
    }

    [Fact]
    public void TheTestsAssessment_IsCollapsedWithItsReason()
    {
        // Act
        string comment = ReviewVerdictComment.Compose(Verdict(
            tests: new ReviewTests(ReviewTestsAssessment.NotNeeded, "Docs only.", [])));

        // Assert
        comment.ShouldContain("<details><summary>Tests: not needed</summary>\n\nDocs only.\n\n</details>");
    }

    [Fact]
    public void TheComment_EndsWithTheShortReviewedCommit()
    {
        // Act
        string comment = ReviewVerdictComment.Compose(Verdict());

        // Assert
        comment.ShouldEndWith("_Reviewed `0123456`._");
    }

    [Fact]
    public void AFindingBodyWithAMentionAndAClosingDetailsTag_IsNeutralized()
    {
        // Act
        string comment = ReviewVerdictComment.Compose(Verdict(
            findings: [Advisory("Ask @octocat </details> before merging.")]));

        // Assert
        comment.ShouldSatisfyAllConditions(
            () => comment.ShouldContain("Ask @\u200Boctocat &lt;/details&gt; before merging."),
            () => comment.ShouldNotContain("@octocat"),
            () => comment.Split("</details>").Length.ShouldBe(comment.Split("<details>").Length));
    }

    [Fact]
    public void AMultiLineSummary_StaysInsideTheAlert()
    {
        // Act
        string comment = ReviewVerdictComment.Compose(Verdict(summary: "First line.\nSecond line."));

        // Assert
        comment.Split('\n')[2].ShouldBe("> First line. Second line.");
    }

    [Fact]
    public void AnOversizedComment_DropsAdvisoryFindingsFromTheEndFirstWithAnOmissionCount()
    {
        // Arrange
        string longPath = new('a', 3_000);
        ReviewFinding[] advisory = [.. Enumerable.Range(1, 30).Select(i => Advisory($"advisory {i}", path: longPath))];

        // Act
        string comment = ReviewVerdictComment.Compose(Verdict(
            ReviewVerdictOutcome.ChangesRequested,
            [Blocking("blocking 1"), .. advisory]));

        // Assert
        comment.ShouldSatisfyAllConditions(
            () => comment.Length.ShouldBeLessThanOrEqualTo(ProviderSafeLength),
            () => comment.ShouldContain("advisory 1 "),
            () => comment.ShouldNotContain("advisory 30 "),
            () => comment.ShouldMatch(@"_\d+ more omitted\._"),
            () => comment.ShouldContain("- `src/Retry.cs:12`: blocking 1"),
            () => comment.ShouldContain("<details><summary>Tests: adequate</summary>"),
            () => comment.Split('\n')[1].ShouldBe("> **Changes requested**: 1 blocking, 30 advisory"));
    }

    [Fact]
    public void AnOversizedComment_DropsBlockingFindingsFromTheEndOnlyAfterEveryAdvisoryFindingAndTheCollapsedSections()
    {
        // Arrange
        string longPath = new('b', 3_000);
        ReviewFinding[] blocking = [.. Enumerable.Range(1, 30).Select(i => Blocking($"blocking {i}", path: longPath))];

        // Act
        string comment = ReviewVerdictComment.Compose(Verdict(
            ReviewVerdictOutcome.ChangesRequested,
            [.. blocking, Advisory("advisory 1")],
            criteria: [new ReviewCriterionAnswer("AC1", ReviewCriterionStatus.Unmet, "No backoff.")]));

        // Assert
        comment.ShouldSatisfyAllConditions(
            () => comment.Length.ShouldBeLessThanOrEqualTo(ProviderSafeLength),
            () => comment.ShouldContain("blocking 1 "),
            () => comment.ShouldNotContain("blocking 30 "),
            () => comment.ShouldNotContain("advisory 1 "),
            () => comment.ShouldNotContain("<details><summary>Criteria evidence</summary>"),
            () => comment.ShouldNotContain("<details><summary>Tests:"),
            () => comment.ShouldContain("**Criteria not met**\n- AC1: unmet\n"),
            () => comment.ShouldMatch(@"- _\d+ more omitted\._"),
            () => comment.Split('\n')[0].ShouldBe("> [!CAUTION]"));
    }

    [Fact]
    public void ACarriedApproval_IsAOneLineNoteNamingTheNewHeadAndTheReviewedCommit()
    {
        // Arrange
        RecordedReviewVerdict carried = Verdict(findings: [Advisory("Consider a constant.")])
            .CarryTo("fedcba9876543210fedcba9876543210fedcba98", WorkerRunId.New());

        // Act
        string comment = ReviewVerdictComment.Compose(carried);

        // Assert
        comment.ShouldBe("Approval carried to `fedcba9`: the change is unchanged since the review of `0123456`.");
    }
}
