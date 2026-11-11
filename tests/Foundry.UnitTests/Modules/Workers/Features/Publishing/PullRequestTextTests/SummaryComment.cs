using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Publishing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Publishing.PullRequestTextTests;

public sealed class SummaryComment
{
    [Fact]
    public void WhenTheVerdictHasADescription_TheCommentIsTheDescriptionWithoutAClosesLine()
    {
        // Arrange
        PullRequestDescription description = PullRequestDescription.TryCreate("Addresses the review: renames the option.")!;

        // Act
        string comment = PullRequestText.SummaryComment(description, ["fix: rename the option"]);

        // Assert
        comment.ShouldBe("Addresses the review: renames the option.");
    }

    [Fact]
    public void WhenTheDescriptionIsMissing_TheCommentListsTheCommitSubjects()
    {
        // Act
        string comment = PullRequestText.SummaryComment(null, ["fix: rename the option", "test: cover the rename"]);

        // Assert
        comment.ShouldBe("## Changes\n\n- fix: rename the option\n- test: cover the rename");
    }

    [Fact]
    public void WhenTheDescriptionAndCommitSubjectsAreMissing_TheCommentIsTheNeutralSentence()
    {
        // Act
        string comment = PullRequestText.SummaryComment(null, []);

        // Assert
        comment.ShouldBe(PullRequestText.NoChangesListedSentence);
    }
}
