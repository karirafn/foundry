using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Features.Reviews;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Reviews.ReviewVerdictCommentTests;

public sealed class ComposeWithInlineFindings
{
    private const string ReviewedCommit = "0123456789abcdef0123456789abcdef01234567";

    private static readonly ReviewFinding InlineBlocking =
        new("src/Retry.cs", 12, ReviewSeverity.Blocking, ReviewCategory.Correctness, null, "Retries forever.");

    private static readonly ReviewFinding SummaryBlocking =
        new("src/Retry.cs", null, ReviewSeverity.Blocking, ReviewCategory.Security, null, "Logs the token.");

    private static readonly ReviewFinding InlineAdvisory =
        new("src/Cache.cs", 3, ReviewSeverity.Advisory, ReviewCategory.Quality, null, "Unbounded cache.");

    private static RecordedReviewVerdict Verdict(params ReviewFinding[] findings) =>
        new(
            "Adds retry.",
            findings,
            [],
            [],
            new ReviewTests(ReviewTestsAssessment.Adequate, "Covered.", []),
            ReviewVerdictOutcome.ChangesRequested,
            ReviewedCommit,
            PatchId: null);

    [Fact]
    public void FindingsPostedInline_StillCountInTheHeadline()
    {
        // Act
        string comment = ReviewVerdictComment.Compose(
            Verdict(InlineBlocking, SummaryBlocking, InlineAdvisory), [InlineBlocking, InlineAdvisory]);

        // Assert
        comment.Split('\n')[1].ShouldBe("> **Changes requested**: 2 blocking, 1 advisory");
    }

    [Fact]
    public void FindingsPostedInline_AreLeftOutOfTheListsAndCountedOnOneLine()
    {
        // Act
        string comment = ReviewVerdictComment.Compose(
            Verdict(InlineBlocking, SummaryBlocking, InlineAdvisory), [InlineBlocking, InlineAdvisory]);

        // Assert
        comment.ShouldSatisfyAllConditions(
            () => comment.ShouldContain("_2 findings are posted inline on the changed lines._"),
            () => comment.ShouldContain("Logs the token."),
            () => comment.ShouldNotContain("Retries forever."),
            () => comment.ShouldNotContain("Unbounded cache."),
            () => comment.ShouldNotContain("Advisory findings"));
    }

    [Fact]
    public void WhenEveryBlockingFindingIsInline_LeavesOutTheBlockingList()
    {
        // Act
        string comment = ReviewVerdictComment.Compose(Verdict(InlineBlocking), [InlineBlocking]);

        // Assert
        comment.ShouldSatisfyAllConditions(
            () => comment.ShouldNotContain("**Blocking**"),
            () => comment.ShouldContain("_1 finding is posted inline on the changed lines._"));
    }

    [Fact]
    public void WhenNoFindingIsInline_RendersAsBefore()
    {
        // Arrange
        RecordedReviewVerdict verdict = Verdict(InlineBlocking, InlineAdvisory);

        // Act
        string comment = ReviewVerdictComment.Compose(verdict, []);

        // Assert
        comment.ShouldBe(ReviewVerdictComment.Compose(verdict));
    }
}
