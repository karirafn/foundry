using Foundry.Modules.Issues.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.ValueObjects.QuestionPostTests;

public sealed class DueAt
{
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void WhenPending_IsDueAtOnce()
    {
        // Arrange
        QuestionPost sut = new QuestionPost.Pending();

        // Act / Assert
        sut.DueAt.ShouldBe(DateTimeOffset.MinValue);
        sut.IsDue(Now).ShouldBeTrue();
    }

    [Fact]
    public void WhenATransientFailureRetriesLater_IsDueAtTheRetryTime()
    {
        // Arrange
        QuestionPost sut = new QuestionPost.Pending().Fail("503", transient: true, Now);

        // Act / Assert
        sut.DueAt.ShouldBe(Now + QuestionPost.FirstRetryDelay);
        sut.IsDue(Now).ShouldBeFalse();
        sut.IsDue(Now + QuestionPost.FirstRetryDelay).ShouldBeTrue();
    }

    [Fact]
    public void WhenAPermanentFailure_IsNeverDue()
    {
        // Arrange
        QuestionPost sut = new QuestionPost.Pending().Fail("404", transient: false, Now);

        // Act / Assert
        sut.DueAt.ShouldBeNull();
        sut.IsDue(DateTimeOffset.MaxValue).ShouldBeFalse();
    }

    [Fact]
    public void WhenPosted_IsNeverDue()
    {
        // Arrange
        QuestionPost sut = new QuestionPost.Posted(1001, Now, CommentUrl: null);

        // Act / Assert
        sut.DueAt.ShouldBeNull();
        sut.IsDue(DateTimeOffset.MaxValue).ShouldBeFalse();
    }
}
