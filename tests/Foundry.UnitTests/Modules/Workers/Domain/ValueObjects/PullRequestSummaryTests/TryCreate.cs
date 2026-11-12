using Foundry.Modules.Workers.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Domain.ValueObjects.PullRequestSummaryTests;

public sealed class TryCreate
{
    [Fact]
    public void WhenTheTextIsASingleLine_KeepsItTrimmed()
    {
        // Act
        PullRequestSummary? summary = PullRequestSummary.TryCreate("  add retry to webhook delivery \t");

        // Assert
        summary.ShouldNotBeNull().Value.ShouldBe("add retry to webhook delivery");
    }

    [Fact]
    public void WhenTheTextIsExactlyTheMaximumLength_IsKept()
    {
        // Arrange
        string text = new('a', PullRequestSummary.MaxLength);

        // Act
        PullRequestSummary? summary = PullRequestSummary.TryCreate(text);

        // Assert
        summary.ShouldNotBeNull().Value.ShouldBe(text);
    }

    [Fact]
    public void WhenTheMaximumLengthIsCountedInCharactersNotUtf16Units_AnEmojiCountsOnce()
    {
        // Arrange — the schema's maxLength counts code points, so the value object counts them too.
        string text = new string('a', PullRequestSummary.MaxLength - 1) + "\U0001F680";

        // Act
        PullRequestSummary? summary = PullRequestSummary.TryCreate(text);

        // Assert
        summary.ShouldNotBeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("add retry\nto webhook delivery")]
    [InlineData("add retry\r\nto webhook delivery")]
    [InlineData("add retry\rto webhook delivery")]
    public void WhenTheTextIsMissingBlankOrMultiLine_ReturnsNull(string? text)
    {
        // Act
        PullRequestSummary? summary = PullRequestSummary.TryCreate(text);

        // Assert
        summary.ShouldBeNull();
    }

    [Fact]
    public void WhenTheTextIsLongerThanTheMaximum_ReturnsNull()
    {
        // Act
        PullRequestSummary? summary = PullRequestSummary.TryCreate(new string('a', PullRequestSummary.MaxLength + 1));

        // Assert
        summary.ShouldBeNull();
    }
}
