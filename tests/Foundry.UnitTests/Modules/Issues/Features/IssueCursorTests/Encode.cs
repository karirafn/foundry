using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Features;
using Foundry.Shared;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Features.IssueCursorTests;

public sealed class Encode
{
    [Fact]
    public void WhenValidInputs_ProducesBase64UrlStringWithNoUnsafeChars()
    {
        // Arrange
        DateTimeOffset detectedAt = new(2026, 6, 26, 12, 0, 0, TimeSpan.Zero);
        IssueId id = IssueId.From(new Guid("12345678-1234-1234-1234-123456789abc"));

        // Act
        string cursor = IssueCursor.Encode(detectedAt, id);

        // Assert
        cursor.ShouldNotContain("+");
        cursor.ShouldNotContain("/");
        cursor.ShouldNotContain("=");
    }

    [Fact]
    public void WhenEncoded_CanBeRoundTripped()
    {
        // Arrange
        DateTimeOffset detectedAt = new(2026, 6, 26, 12, 0, 0, TimeSpan.Zero);
        IssueId id = IssueId.From(Guid.NewGuid());

        // Act
        string cursor = IssueCursor.Encode(detectedAt, id);
        Result<(DateTimeOffset DetectedAt, IssueId Id)> decoded = IssueCursor.Decode(cursor);

        // Assert
        Result<(DateTimeOffset DetectedAt, IssueId Id)>.Success success =
            decoded.ShouldBeOfType<Result<(DateTimeOffset DetectedAt, IssueId Id)>.Success>();
        success.Value.DetectedAt.ShouldBe(detectedAt);
        success.Value.Id.ShouldBe(id);
    }

    [Fact]
    public void WhenEncoded_DetectedAtPreservesUtcInstant()
    {
        // Arrange — offset-aware input; stored as UTC so the decoded value equals the UTC equivalent
        DateTimeOffset detectedAt = new(2026, 6, 26, 12, 0, 0, TimeSpan.Zero);
        IssueId id = IssueId.From(Guid.NewGuid());

        // Act
        string cursor = IssueCursor.Encode(detectedAt, id);
        Result<(DateTimeOffset DetectedAt, IssueId Id)> decoded = IssueCursor.Decode(cursor);

        // Assert
        Result<(DateTimeOffset DetectedAt, IssueId Id)>.Success success =
            decoded.ShouldBeOfType<Result<(DateTimeOffset DetectedAt, IssueId Id)>.Success>();
        success.Value.DetectedAt.UtcDateTime.ShouldBe(detectedAt.UtcDateTime);
    }
}
