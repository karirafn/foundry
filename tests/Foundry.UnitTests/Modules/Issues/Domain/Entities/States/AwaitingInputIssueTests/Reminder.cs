using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.States.AwaitingInputIssueTests;

public sealed class Reminder
{
    private static readonly DateTimeOffset PostedAt = new(2026, 11, 11, 10, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Delay = TimeSpan.FromDays(3);

    [Fact]
    public void WhenEntered_NoReminderIsSent()
    {
        // Act
        AwaitingInputIssue awaiting = new IssueBuilder().AwaitingInput();

        // Assert
        awaiting.Reminder.ShouldBeOfType<QuestionReminder.Unsent>();
    }

    [Fact]
    public void WhenUnsent_ItIsDueTheDelayAfterThePost()
    {
        // Arrange
        AwaitingInputIssue awaiting = new IssueBuilder().AwaitingInputPosted(PostedAt);

        // Act
        DateTimeOffset? dueAt = awaiting.Reminder.DueAt(PostedAt, Delay);

        // Assert
        dueAt.ShouldBe(PostedAt + Delay);
    }

    [Fact]
    public void WhenSent_RecordsTheCommentAndTimeAndIsNeverDueAgain()
    {
        // Arrange
        AwaitingInputIssue awaiting = new IssueBuilder().AwaitingInputPosted(PostedAt);
        DateTimeOffset sentAt = PostedAt + Delay;

        // Act
        awaiting.RecordReminderPosted(2002, sentAt);

        // Assert
        QuestionReminder.Sent sent = awaiting.Reminder.ShouldBeOfType<QuestionReminder.Sent>();
        sent.ShouldSatisfyAllConditions(
            () => sent.CommentId.ShouldBe(2002),
            () => sent.SentAt.ShouldBe(sentAt),
            () => awaiting.Reminder.DueAt(PostedAt, Delay).ShouldBeNull());
    }

    [Fact]
    public void WhenSentTwice_KeepsTheFirstReminder()
    {
        // Arrange
        AwaitingInputIssue awaiting = new IssueBuilder().AwaitingInputPosted(PostedAt);
        awaiting.RecordReminderPosted(2002, PostedAt + Delay);

        // Act
        awaiting.RecordReminderPosted(3003, PostedAt + Delay + Delay);

        // Assert
        awaiting.Reminder.ShouldBeOfType<QuestionReminder.Sent>().CommentId.ShouldBe(2002);
    }

    [Fact]
    public void WhenATransientReminderFailureRepeats_ItRetriesWithTheQuestionPostsBackoff()
    {
        // Arrange
        AwaitingInputIssue awaiting = new IssueBuilder().AwaitingInputPosted(PostedAt);
        DateTimeOffset now = PostedAt + Delay;

        // Act
        awaiting.RecordReminderPostFailed("503", transient: true, now);
        DateTimeOffset? first = awaiting.Reminder.DueAt(PostedAt, Delay);
        awaiting.RecordReminderPostFailed("503", transient: true, now);
        DateTimeOffset? second = awaiting.Reminder.DueAt(PostedAt, Delay);

        // Assert
        first.ShouldBe(now + QuestionPost.FirstRetryDelay);
        second.ShouldBe(now + (QuestionPost.FirstRetryDelay * 2));
    }

    [Fact]
    public void WhenTheReminderFailsPermanently_ItIsNeverDue()
    {
        // Arrange
        AwaitingInputIssue awaiting = new IssueBuilder().AwaitingInputPosted(PostedAt);

        // Act
        awaiting.RecordReminderPostFailed("403", transient: false, PostedAt + Delay);

        // Assert
        awaiting.Reminder.DueAt(PostedAt, Delay).ShouldBeNull();
    }
}
