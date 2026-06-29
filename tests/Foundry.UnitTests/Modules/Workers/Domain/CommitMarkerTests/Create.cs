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
}
