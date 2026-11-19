using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Features.Reviews;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Reviews.ReviewVerdictCommentTests;

public sealed class InlineBody
{
    [Fact]
    public void AFindingWithARule_ReadsAsItsSummaryLineWithoutTheLocation()
    {
        // Arrange
        ReviewFinding finding = new(
            "src/Retry.cs", 12, ReviewSeverity.Blocking, ReviewCategory.Correctness, new ReviewRule("code.1", null, null), "Retries forever.");

        // Act
        string body = ReviewVerdictComment.InlineBody(finding);

        // Assert
        body.ShouldBe("Retries forever. (correctness · code.1)");
    }

    [Fact]
    public void AFindingWithoutARule_NamesOnlyItsCategory()
    {
        // Arrange
        ReviewFinding finding = new("src/Retry.cs", 12, ReviewSeverity.Advisory, ReviewCategory.Quality, null, "Long method.");

        // Act / Assert
        ReviewVerdictComment.InlineBody(finding).ShouldBe("Long method. (quality)");
    }

    [Fact]
    public void ModelWrittenMentionsAndHtml_AreNeutralized()
    {
        // Arrange
        ReviewFinding finding = new(
            "src/Retry.cs", 12, ReviewSeverity.Blocking, ReviewCategory.Correctness, null, "Ask @alice about </details> <img src=x>.");

        // Act
        string body = ReviewVerdictComment.InlineBody(finding);

        // Assert
        body.ShouldBe("Ask @\u200Balice about &lt;/details&gt; &lt;img src=x&gt;. (correctness)");
    }
}
