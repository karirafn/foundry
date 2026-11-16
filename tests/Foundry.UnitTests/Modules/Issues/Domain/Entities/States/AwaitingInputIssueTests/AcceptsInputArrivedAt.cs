using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.States.AwaitingInputIssueTests;

public sealed class AcceptsInputArrivedAt
{
    private static readonly DateTimeOffset PostedAt = new(2026, 11, 11, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void WhenTheInputArrivedAfterThePost_IsTrue()
    {
        // Arrange
        AwaitingInputIssue awaiting = new IssueBuilder().AwaitingInputPosted(PostedAt);

        // Act
        bool accepts = awaiting.AcceptsInputArrivedAt(PostedAt.AddTicks(1));

        // Assert
        accepts.ShouldBeTrue();
    }

    [Fact]
    public void WhenTheInputArrivedAtThePostTime_IsFalse()
    {
        // Arrange
        AwaitingInputIssue awaiting = new IssueBuilder().AwaitingInputPosted(PostedAt);

        // Act
        bool accepts = awaiting.AcceptsInputArrivedAt(PostedAt);

        // Assert
        accepts.ShouldBeFalse();
    }

    [Fact]
    public void WhenTheInputArrivedBeforeThePost_IsFalse()
    {
        // Arrange
        AwaitingInputIssue awaiting = new IssueBuilder().AwaitingInputPosted(PostedAt);

        // Act
        bool accepts = awaiting.AcceptsInputArrivedAt(PostedAt.AddMinutes(-5));

        // Assert
        accepts.ShouldBeFalse();
    }

    [Fact]
    public void WhenTheQuestionIsNotPosted_IsFalse()
    {
        // Arrange
        AwaitingInputIssue awaiting = new IssueBuilder().AwaitingInput();

        // Act
        bool accepts = awaiting.AcceptsInputArrivedAt(PostedAt.AddMinutes(5));

        // Assert
        accepts.ShouldBeFalse();
    }
}
