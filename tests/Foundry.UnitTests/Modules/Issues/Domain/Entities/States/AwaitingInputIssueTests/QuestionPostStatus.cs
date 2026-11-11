using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.States.AwaitingInputIssueTests;

public sealed class QuestionPostStatus
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void WhenEntered_ThePostIsPendingAndDue()
    {
        // Act
        AwaitingInputIssue awaiting = new IssueBuilder().AwaitingInput();

        // Assert
        awaiting.QuestionPost.ShouldBeOfType<QuestionPost.Pending>().IsDue(Now).ShouldBeTrue();
    }

    [Fact]
    public void WhenATransientPostFailureRepeats_TheRetryBacksOff()
    {
        // Arrange
        AwaitingInputIssue awaiting = new IssueBuilder().AwaitingInput();

        // Act
        awaiting.RecordQuestionPostFailed("503", transient: true, Now);
        QuestionPost.Failed first = awaiting.QuestionPost.ShouldBeOfType<QuestionPost.Failed>();
        awaiting.RecordQuestionPostFailed("503", transient: true, Now);
        QuestionPost.Failed second = awaiting.QuestionPost.ShouldBeOfType<QuestionPost.Failed>();

        // Assert
        first.ShouldSatisfyAllConditions(
            () => first.NextAttemptAt.ShouldBe(Now + QuestionPost.FirstRetryDelay),
            () => second.NextAttemptAt.ShouldBe(Now + (QuestionPost.FirstRetryDelay * 2)),
            () => second.Attempts.ShouldBe(2),
            () => awaiting.QuestionPost.IsDue(Now).ShouldBeFalse());
    }

    [Fact]
    public void WhenAPostFailsPermanently_ItIsNeverDue()
    {
        // Arrange
        AwaitingInputIssue awaiting = new IssueBuilder().AwaitingInput();

        // Act
        awaiting.RecordQuestionPostFailed("403", transient: false, Now);

        // Assert
        awaiting.QuestionPost.ShouldBeOfType<QuestionPost.Failed>().NextAttemptAt.ShouldBeNull();
        awaiting.QuestionPost.IsDue(DateTimeOffset.MaxValue).ShouldBeFalse();
    }

    [Fact]
    public void WhenPosted_KeepsTheFirstCommentAndIgnoresALateFailure()
    {
        // Arrange
        AwaitingInputIssue awaiting = new IssueBuilder().AwaitingInput();

        // Act
        awaiting.RecordQuestionPosted(42, Now, "https://github.com/o/r/pull/1#issuecomment-42");
        awaiting.RecordQuestionPosted(43, Now.AddMinutes(1), commentUrl: null);
        awaiting.RecordQuestionPostFailed("503", transient: true, Now);

        // Assert
        QuestionPost.Posted posted = awaiting.QuestionPost.ShouldBeOfType<QuestionPost.Posted>();
        posted.ShouldSatisfyAllConditions(
            () => posted.CommentId.ShouldBe(42),
            () => posted.PostedAt.ShouldBe(Now));
    }
}
