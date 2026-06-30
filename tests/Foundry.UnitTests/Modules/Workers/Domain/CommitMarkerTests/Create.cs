using Foundry.Modules.Workers.Domain;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Domain.CommitMarkerTests;

public sealed class Create
{
    [Fact]
    public void WhenAllFieldsProvided_CreatesCommitMarker()
    {
        // Arrange
        DateTimeOffset observedAt = new(2026, 6, 29, 10, 0, 0, TimeSpan.Zero);
        string sha = "abc1234";
        string message = "feat: add something";

        // Act
        CommitMarker marker = CommitMarker.Create(observedAt, sha, message);

        // Assert
        marker.ShouldSatisfyAllConditions(
            () => marker.ObservedAt.ShouldBe(observedAt),
            () => marker.Sha.ShouldBe(sha),
            () => marker.Message.ShouldBe(message));
    }

    [Fact]
    public void WhenTwoMarkersHaveSameFields_AreEqual()
    {
        // Arrange
        DateTimeOffset observedAt = new(2026, 6, 29, 10, 0, 0, TimeSpan.Zero);

        // Act
        CommitMarker first = CommitMarker.Create(observedAt, "abc1234", "feat: add something");
        CommitMarker second = CommitMarker.Create(observedAt, "abc1234", "feat: add something");

        // Assert
        first.ShouldBe(second);
    }

    [Fact]
    public void WhenTwoMarkersHaveDifferentSha_AreNotEqual()
    {
        // Arrange
        DateTimeOffset observedAt = new(2026, 6, 29, 10, 0, 0, TimeSpan.Zero);

        // Act
        CommitMarker first = CommitMarker.Create(observedAt, "abc1234", "feat: add something");
        CommitMarker second = CommitMarker.Create(observedAt, "xyz9999", "feat: add something");

        // Assert
        first.ShouldNotBe(second);
    }

    [Fact]
    public void WhenMessageExceedsMaxLength_TruncatesToMaxLength()
    {
        // Arrange
        DateTimeOffset observedAt = new(2026, 6, 29, 10, 0, 0, TimeSpan.Zero);
        string sha = "abc1234";
        string longMessage = new('x', CommitMarker.MessageMaxLength + 50);

        // Act
        CommitMarker marker = CommitMarker.Create(observedAt, sha, longMessage);

        // Assert
        marker.Message.Length.ShouldBe(CommitMarker.MessageMaxLength);
    }

    [Fact]
    public void WhenShaExceedsMaxLength_TruncatesToMaxLength()
    {
        // Arrange
        DateTimeOffset observedAt = new(2026, 6, 29, 10, 0, 0, TimeSpan.Zero);
        string longSha = new('a', CommitMarker.ShaMaxLength + 10);
        string message = "feat: add something";

        // Act
        CommitMarker marker = CommitMarker.Create(observedAt, longSha, message);

        // Assert
        marker.Sha.Length.ShouldBe(CommitMarker.ShaMaxLength);
    }

    [Fact]
    public void WhenShaIsEmpty_ThrowsArgumentException()
    {
        // Arrange
        DateTimeOffset observedAt = new(2026, 6, 29, 10, 0, 0, TimeSpan.Zero);

        // Act
        ArgumentException exception = Should.Throw<ArgumentException>(
            () => CommitMarker.Create(observedAt, string.Empty, "feat: add something"));

        // Assert
        exception.ParamName.ShouldBe("sha");
    }

    [Fact]
    public void WhenMessageIsEmpty_ThrowsArgumentException()
    {
        // Arrange
        DateTimeOffset observedAt = new(2026, 6, 29, 10, 0, 0, TimeSpan.Zero);

        // Act
        ArgumentException exception = Should.Throw<ArgumentException>(
            () => CommitMarker.Create(observedAt, "abc1234", string.Empty));

        // Assert
        exception.ParamName.ShouldBe("message");
    }
}
