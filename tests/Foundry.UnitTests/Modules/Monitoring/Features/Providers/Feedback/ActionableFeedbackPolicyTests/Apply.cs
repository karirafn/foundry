using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers.Comments;
using Foundry.Modules.Monitoring.Features.Providers.Feedback;

using Foundry.Testing;

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
        ReviewFeedback result = sut.Apply([oldComment, midComment, newComment], Since, PostedCommentIds.None);

        // Assert
        result.NewestCommentAt.ShouldBe(newest);
    }

    [Fact]
    public void WhenInputIsEmpty_ReturnsEmptyFeedbackWithZeroOmittedAndNullNewest()
    {
        // Arrange
        ActionableFeedbackPolicy sut = BuildSut();

        // Act
        ReviewFeedback result = sut.Apply([], Since, PostedCommentIds.None);

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
        ReviewFeedback result = sut.Apply(comments, since, PostedCommentIds.None);

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
        ReviewFeedback result = sut.Apply([comment1, comment2], since, PostedCommentIds.None);

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
        ReviewFeedback result = sut.Apply([selfComment], Since, PostedCommentIds.None);

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
        ReviewFeedback result = sut.Apply([editedAfterCutoff], Since, PostedCommentIds.None);

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
        ReviewFeedback result = sut.Apply([atBoundary, beforeBoundary], Since, PostedCommentIds.None);

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
        ReviewFeedback result = sut.Apply([afterBoundary], Since, PostedCommentIds.None);

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
        ReviewFeedback result = sut.Apply([resolvedThreadComment], Since, PostedCommentIds.None);

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
        ReviewFeedback result = sut.Apply([botComment], Since, PostedCommentIds.None);

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
        ReviewFeedback result = sut.Apply([systemNote], Since, PostedCommentIds.None);

        // Assert
        result.Comments.ShouldBeEmpty();
    }

    [Fact]
    public void WhenCommentsAreHeldOnlyByQuietPeriod_HeldBackUntilIsNewestHeldCreatedAtPlusQuietPeriod()
    {
        // Arrange — a burst of comments inside the quiet period, plus one already actionable
        DateTimeOffset older = Now.AddSeconds(-90);
        DateTimeOffset newest = Now.AddSeconds(-5);
        ProviderComment actionable = new ProviderCommentBuilder().WithCreatedAt(Now.AddMinutes(-10)).Build();
        ProviderComment heldOlder = new ProviderCommentBuilder().WithCreatedAt(older).Build();
        ProviderComment heldNewest = new ProviderCommentBuilder().WithCreatedAt(newest).Build();

        ActionableFeedbackPolicy sut = BuildSut();

        // Act
        ReviewFeedback result = sut.Apply([heldNewest, actionable, heldOlder], Since, PostedCommentIds.None);

        // Assert
        result.ShouldSatisfyAllConditions(
            () => result.Comments.Count.ShouldBe(1),
            () => result.HeldBackUntil.ShouldBe(newest + ActionableFeedbackPolicy.QuietPeriod));
    }

    [Fact]
    public void WhenNoCommentIsHeldByQuietPeriod_HeldBackUntilIsNull()
    {
        // Arrange
        ProviderComment actionable = new ProviderCommentBuilder().WithCreatedAt(Now.AddMinutes(-10)).Build();

        ActionableFeedbackPolicy sut = BuildSut();

        // Act
        ReviewFeedback result = sut.Apply([actionable], Since, PostedCommentIds.None);

        // Assert
        result.HeldBackUntil.ShouldBeNull();
    }

    [Fact]
    public void WhenRecentCommentIsExcludedForAnotherReason_HeldBackUntilIsNull()
    {
        // Arrange — recent comments that would never become actionable must not schedule a follow-up
        ProviderComment bot = new ProviderCommentBuilder().WithAuthorIsBot(true).WithCreatedAt(Now.AddSeconds(-5)).Build();
        ProviderComment system = new ProviderCommentBuilder().WithIsSystem(true).WithCreatedAt(Now.AddSeconds(-5)).Build();
        ProviderComment resolved = new ProviderCommentBuilder().WithThreadResolved(true).WithCreatedAt(Now.AddSeconds(-5)).Build();

        ActionableFeedbackPolicy sut = BuildSut();

        // Act
        ReviewFeedback result = sut.Apply([bot, system, resolved], Now.AddMinutes(-10), PostedCommentIds.None);

        // Assert
        result.HeldBackUntil.ShouldBeNull();
    }

    [Fact]
    public void WhenRunAtHeldBackUntil_HeldCommentsBecomeActionable()
    {
        // Arrange — the follow-up review pass runs when the deferred nudge comes due
        DateTimeOffset older = Now.AddSeconds(-30);
        DateTimeOffset newest = Now.AddSeconds(-5);
        ProviderComment heldOlder = new ProviderCommentBuilder().WithCreatedAt(older).WithBody("first").Build();
        ProviderComment heldNewest = new ProviderCommentBuilder().WithCreatedAt(newest).WithBody("second").Build();

        ReviewFeedback first = BuildSut().Apply([heldOlder, heldNewest], Since, PostedCommentIds.None);
        DateTimeOffset dueAt = first.HeldBackUntil.ShouldNotBeNull();

        // Act
        ReviewFeedback followUp = BuildSut(dueAt).Apply([heldOlder, heldNewest], Since, PostedCommentIds.None);

        // Assert
        followUp.ShouldSatisfyAllConditions(
            () => first.Comments.ShouldBeEmpty(),
            () => followUp.Comments.Select(c => c.Body).ShouldBe(["first", "second"]),
            () => followUp.NewestCommentAt.ShouldBe(newest),
            () => followUp.HeldBackUntil.ShouldBeNull());
    }

    [Fact]
    public void WhenCommentIdIsInPostedRegistry_SkipsItWhoeverItsAuthorIs()
    {
        // Arrange — Foundry posts with the PAT owner's account, so the registered comment looks human
        ProviderComment posted = new ProviderCommentBuilder().WithId(500).WithAuthorLogin("octocat").WithBody("posted").WithCreatedAt(Since.AddHours(1)).Build();
        ProviderComment reply = new ProviderCommentBuilder().WithId(501).WithAuthorLogin("octocat").WithBody("reply").WithCreatedAt(Since.AddHours(2)).Build();
        PostedCommentIds registry = new([ProviderCommentId.Create(500).ValueOrThrow()]);

        ActionableFeedbackPolicy sut = BuildSut();

        // Act
        ReviewFeedback result = sut.Apply([posted, reply], Since, registry);

        // Assert
        result.Comments.Select(c => c.Body).ShouldBe(["reply"]);
    }

    [Fact]
    public void WhenPostedCommentIsInsideQuietPeriod_HeldBackUntilIsNull()
    {
        // Arrange — a registered comment never becomes actionable, so it must not schedule a follow-up
        ProviderComment posted = new ProviderCommentBuilder().WithId(500).WithCreatedAt(Now.AddSeconds(-5)).Build();
        PostedCommentIds registry = new([ProviderCommentId.Create(500).ValueOrThrow()]);

        ActionableFeedbackPolicy sut = BuildSut();

        // Act
        ReviewFeedback result = sut.Apply([posted], Now.AddMinutes(-10), registry);

        // Assert
        result.HeldBackUntil.ShouldBeNull();
    }

    [Theory]
    [InlineData(nameof(CommentOrigin.ReviewThread))]
    [InlineData(nameof(CommentOrigin.ReviewSummary))]
    public void WhenRegisteredIdMatchesCommentOutsideConversation_KeepsIt(string originName)
    {

        // Arrange — GitHub numbers reviews and review comments apart from issue comments
        CommentOrigin origin = Enum.Parse<CommentOrigin>(originName);
        ProviderComment review = new ProviderCommentBuilder().WithId(500).WithOrigin(origin).WithCreatedAt(Since.AddHours(1)).Build();
        PostedCommentIds registry = new([ProviderCommentId.Create(500).ValueOrThrow()]);

        ActionableFeedbackPolicy sut = BuildSut();

        // Act
        ReviewFeedback result = sut.Apply([review], Since, registry);

        // Assert
        result.Comments.Count.ShouldBe(1);
    }

    [Fact]
    public void WhenCommentHasNoId_KeepsIt()
    {
        // Arrange
        ProviderComment comment = new ProviderCommentBuilder().WithId(null).WithCreatedAt(Since.AddHours(1)).Build();
        PostedCommentIds registry = new([ProviderCommentId.Create(500).ValueOrThrow()]);

        ActionableFeedbackPolicy sut = BuildSut();

        // Act
        ReviewFeedback result = sut.Apply([comment], Since, registry);

        // Assert
        result.Comments.Count.ShouldBe(1);
    }
}
