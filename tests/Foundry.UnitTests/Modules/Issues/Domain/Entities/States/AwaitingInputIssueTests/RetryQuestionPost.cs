using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.States.AwaitingInputIssueTests;

public sealed class RetryQuestionPost
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void WhenThePostFailedPermanently_ReturnsItToPending()
    {
        // Arrange
        AwaitingInputIssue awaiting = new IssueBuilder().AwaitingInput();
        awaiting.RecordQuestionPostFailed("404", transient: false, Now);

        // Act
        bool retried = awaiting.RetryQuestionPost();

        // Assert
        retried.ShouldBeTrue();
        awaiting.QuestionPost.ShouldBeOfType<QuestionPost.Pending>().IsDue(Now).ShouldBeTrue();
    }

    [Fact]
    public void WhenAPostFailedTransientlyAndWaitsOnItsBackoff_ReturnsItToPendingAtOnce()
    {
        // Arrange
        AwaitingInputIssue awaiting = new IssueBuilder().AwaitingInput();
        awaiting.RecordQuestionPostFailed("503", transient: true, Now);

        // Act
        bool retried = awaiting.RetryQuestionPost();

        // Assert
        retried.ShouldBeTrue();
        awaiting.QuestionPost.ShouldBeOfType<QuestionPost.Pending>();
    }

    [Fact]
    public void WhenThePostIsPending_ChangesNothing()
    {
        // Arrange
        AwaitingInputIssue awaiting = new IssueBuilder().AwaitingInput();

        // Act
        bool retried = awaiting.RetryQuestionPost();

        // Assert
        retried.ShouldBeFalse();
        awaiting.QuestionPost.ShouldBeOfType<QuestionPost.Pending>();
    }

    [Fact]
    public void WhenTheQuestionIsPosted_ChangesNothing()
    {
        // Arrange
        AwaitingInputIssue awaiting = new IssueBuilder().AwaitingInputPosted(Now);

        // Act
        bool retried = awaiting.RetryQuestionPost();

        // Assert
        retried.ShouldBeFalse();
        awaiting.QuestionPost.ShouldBeOfType<QuestionPost.Posted>().PostedAt.ShouldBe(Now);
    }
}
