using Foundry.Modules.Monitoring.Features.Providers.Feedback;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.Providers.Feedback.ActionableFeedbackPolicyTests;

public sealed class Apply
{
    private static readonly DateTimeOffset Since = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    // Fixed point in time where "now" is well past the quiet period window for all non-recent tests.
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    private static ActionableFeedbackPolicy BuildSut(DateTimeOffset? fixedNow = null)
    {
        FakeTimeProvider timeProvider = new(fixedNow ?? Now);
        return new ActionableFeedbackPolicy(timeProvider);
    }

    [Fact]
    public void WhenCommentsAreKept_NewestCommentAtEqualsMaxCreatedAtOfKeptSet()
    {
        // Arrange — three comments with distinct times, all past cutoff and quiet period
        DateTimeOffset oldest = Since.AddHours(1);
        DateTimeOffset middle = Since.AddHours(2);
        DateTimeOffset newest = Since.AddHours(3);

        ProviderComment oldComment = new ProviderCommentBuilder().WithCreatedAt(oldest).Build();
        ProviderComment midComment = new ProviderCommentBuilder().WithCreatedAt(middle).Build();
        ProviderComment newComment = new ProviderCommentBuilder().WithCreatedAt(newest).Build();

        ActionableFeedbackPolicy sut = BuildSut();

        // Act
        ReviewFeedback result = sut.Apply([oldComment, midComment, newComment], Since);

        // Assert
        result.NewestCommentAt.ShouldBe(newest);
    }

    [Fact]
    public void WhenInputIsEmpty_ReturnsEmptyFeedbackWithZeroOmittedAndNullNewest()
    {
        // Arrange
        ActionableFeedbackPolicy sut = BuildSut();

        // Act
        ReviewFeedback result = sut.Apply([], Since);

        // Assert
        result.ShouldSatisfyAllConditions(
            () => result.Comments.ShouldBeEmpty(),
            () => result.OmittedCommentCount.ShouldBe(0),
            () => result.NewestCommentAt.ShouldBeNull());
    }

    [Fact]
    public void WhenMoreThan50SurvivorsExist_KeepsNewest50AndRecordsOmittedCount()
    {
        // Arrange — 60 comments past the cutoff and past the quiet period
        DateTimeOffset fixedNow = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
        DateTimeOffset since = fixedNow.AddDays(-10);
        DateTimeOffset quietCutoff = fixedNow.AddMinutes(-2);

        List<ProviderComment> comments = [];
        for (int i = 1; i <= 60; i++)
        {
            // Space them 5 minutes apart to stay clearly outside quiet period
            comments.Add(new ProviderCommentBuilder()
                .WithCreatedAt(quietCutoff.AddMinutes(-(i * 5)))
                .WithBody($"Comment {i}")
                .Build());
        }

        ActionableFeedbackPolicy sut = BuildSut(fixedNow);

        // Act
        ReviewFeedback result = sut.Apply(comments, since);

        // Assert
        result.ShouldSatisfyAllConditions(
            () => result.Comments.Count.ShouldBe(50),
            () => result.OmittedCommentCount.ShouldBe(10));
    }

    [Fact]
    public void WhenAllCommentsAreWithinQuietPeriod_ReturnsEmptyResult()
    {
        // Arrange — now = 12:00, quiet period = 2 min, so comments after 11:58 are held
        DateTimeOffset fixedNow = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
        DateTimeOffset recentComment1 = fixedNow.AddSeconds(-30);
        DateTimeOffset recentComment2 = fixedNow.AddSeconds(-90);

        ProviderComment comment1 = new ProviderCommentBuilder()
            .WithCreatedAt(recentComment1)
            .Build();
        ProviderComment comment2 = new ProviderCommentBuilder()
            .WithCreatedAt(recentComment2)
            .Build();

        DateTimeOffset since = fixedNow.AddDays(-1); // cutoff is well before
        ActionableFeedbackPolicy sut = BuildSut(fixedNow);

        // Act
        ReviewFeedback result = sut.Apply([comment1, comment2], since);

        // Assert
        result.Comments.ShouldBeEmpty();
    }

    // The policy never filters by author login — self-comments (same login as credential) pass through.
    [Fact]
    public void WhenCommentAuthorIsSameLoginAsCredentialAndNotBot_KeepsComment()
    {
        // Arrange — same login as an arbitrary credential, not a bot
        ProviderComment selfComment = new ProviderCommentBuilder()
            .WithAuthorLogin("foundry-bot")
            .WithAuthorIsBot(false)
            .WithCreatedAt(Since.AddHours(1))
            .Build();

        ActionableFeedbackPolicy sut = BuildSut();

        // Act
        ReviewFeedback result = sut.Apply([selfComment], Since);

        // Assert
        result.Comments.Count.ShouldBe(1);
    }

    // ProviderComment has no updatedAt field. A comment created before the cutoff
    // but edited after it is structurally excluded because only createdAt is compared.
    [Fact]
    public void WhenCreatedAtIsBeforeSinceRegardlessOfEditTime_ExcludesComment()
    {
        // Arrange — only createdAt matters; no updatedAt field exists on ProviderComment
        ProviderComment editedAfterCutoff = new ProviderCommentBuilder()
            .WithCreatedAt(Since.AddHours(-1))   // created before cutoff
            .WithBody("Edited after cutoff but created before — excluded by createdAt")
            .Build();

        ActionableFeedbackPolicy sut = BuildSut();

        // Act
        ReviewFeedback result = sut.Apply([editedAfterCutoff], Since);

        // Assert
        result.Comments.ShouldBeEmpty();
    }

    [Fact]
    public void WhenCreatedAtIsAtOrBeforeSince_ExcludesComment()
    {
        // Arrange — comment created exactly at the since boundary (not strictly after)
        ProviderComment atBoundary = new ProviderCommentBuilder()
            .WithCreatedAt(Since)
            .Build();
        ProviderComment beforeBoundary = new ProviderCommentBuilder()
            .WithCreatedAt(Since.AddSeconds(-1))
            .Build();

        ActionableFeedbackPolicy sut = BuildSut();

        // Act
        ReviewFeedback result = sut.Apply([atBoundary, beforeBoundary], Since);

        // Assert
        result.Comments.ShouldBeEmpty();
    }

    [Fact]
    public void WhenCreatedAtIsStrictlyAfterSince_KeepsComment()
    {
        // Arrange
        ProviderComment afterBoundary = new ProviderCommentBuilder()
            .WithBody("Important feedback")
            .WithCreatedAt(Since.AddSeconds(1))
            .Build();

        ActionableFeedbackPolicy sut = BuildSut();

        // Act
        ReviewFeedback result = sut.Apply([afterBoundary], Since);

        // Assert
        result.Comments.Count.ShouldBe(1);
        result.Comments[0].Body.ShouldBe("Important feedback");
    }

    [Fact]
    public void WhenCommentIsInResolvedThread_ExcludesIt()
    {
        // Arrange
        ProviderComment resolvedThreadComment = new ProviderCommentBuilder()
            .WithThreadResolved(true)
            .WithOrigin(CommentOrigin.ReviewThread)
            .WithCreatedAt(Since.AddHours(1))
            .Build();

        ActionableFeedbackPolicy sut = BuildSut();

        // Act
        ReviewFeedback result = sut.Apply([resolvedThreadComment], Since);

        // Assert
        result.Comments.ShouldBeEmpty();
    }

    [Fact]
    public void WhenCommentIsBotAuthored_ExcludesIt()
    {
        // Arrange
        ProviderComment botComment = new ProviderCommentBuilder()
            .WithAuthorIsBot(true)
            .WithCreatedAt(Since.AddHours(1))
            .Build();

        ActionableFeedbackPolicy sut = BuildSut();

        // Act
        ReviewFeedback result = sut.Apply([botComment], Since);

        // Assert
        result.Comments.ShouldBeEmpty();
    }

    [Fact]
    public void WhenCommentIsSystemNote_ExcludesIt()
    {
        // Arrange
        ProviderComment systemNote = new ProviderCommentBuilder()
            .WithIsSystem(true)
            .WithCreatedAt(Since.AddHours(1))
            .Build();

        ActionableFeedbackPolicy sut = BuildSut();

        // Act
        ReviewFeedback result = sut.Apply([systemNote], Since);

        // Assert
        result.Comments.ShouldBeEmpty();
    }
}
