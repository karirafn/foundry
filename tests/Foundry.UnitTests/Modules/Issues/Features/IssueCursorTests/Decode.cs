using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Features;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Features.IssueCursorTests;

public sealed class Decode
{
    [Theory]
    [InlineData("")]
    [InlineData("notbase64url!!!")]
    [InlineData("aGVsbG8=")]              // valid base64 but no separator
    [InlineData("bm9zZXBhcmF0b3I=")]     // "noseparator"
    public void WhenMalformedCursor_ReturnsFailure(string malformed)
    {
        // Arrange — input

        // Act
        Result<(DateTimeOffset DetectedAt, IssueId Id)> result = IssueCursor.Decode(malformed);

        // Assert
        result.ShouldBeOfType<Result<(DateTimeOffset DetectedAt, IssueId Id)>.Failure>();
    }

    [Fact]
    public void WhenMalformedCursor_DoesNotThrow()
    {
        // Arrange
        const string malformed = "this-is-not-a-valid-cursor-at-all$$$$";

        // Act
        Result<(DateTimeOffset DetectedAt, IssueId Id)> result =
            Should.NotThrow(() => IssueCursor.Decode(malformed));

        // Assert
        result.ShouldBeOfType<Result<(DateTimeOffset DetectedAt, IssueId Id)>.Failure>();
    }

    [Fact]
    public void WhenMalformedCursor_FailureHasInvalidCursorCode()
    {
        // Arrange
        const string malformed = "dGhpcy1pcy1ub3QtdmFsaWQ="; // "this-is-not-valid"

        // Act
        Result<(DateTimeOffset DetectedAt, IssueId Id)> result = IssueCursor.Decode(malformed);

        // Assert
        Result<(DateTimeOffset DetectedAt, IssueId Id)>.Failure failure =
            result.ShouldBeOfType<Result<(DateTimeOffset DetectedAt, IssueId Id)>.Failure>();
        failure.Error.Code.ShouldBe(IssueErrors.InvalidCursorCode);
    }
}
