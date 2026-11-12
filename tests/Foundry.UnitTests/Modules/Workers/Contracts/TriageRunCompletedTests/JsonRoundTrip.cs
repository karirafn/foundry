using System.Text.Json;

using Foundry.Modules.Workers.Contracts;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Contracts.TriageRunCompletedTests;

public sealed class JsonRoundTrip
{
    [Fact]
    public void WhenBlockedResultIsSerialized_EmitsTheBlockedDiscriminator()
    {
        // Arrange
        TriageResult result = new TriageResult.Blocked(
            TriageKind.Feature,
            [new TriageBlocker(15, "needs the retry policy")]);

        // Act
        string json = JsonSerializer.Serialize(result);

        // Assert
        JsonDocument.Parse(json).RootElement.GetProperty("$type").GetString().ShouldBe("blocked");
    }

    [Fact]
    public void WhenBlockedResultIsRoundTripped_KeepsKindAndBlockersInOrder()
    {
        // Arrange
        TriageResult original = new TriageResult.Blocked(
            TriageKind.Bug,
            [new TriageBlocker(31, "second by number, first named"), new TriageBlocker(15, "needs the retry policy")]);

        // Act
        TriageResult? deserialized = JsonSerializer.Deserialize<TriageResult>(JsonSerializer.Serialize(original));

        // Assert
        TriageResult.Blocked blocked = deserialized.ShouldBeOfType<TriageResult.Blocked>();
        blocked.ShouldSatisfyAllConditions(
            () => blocked.Kind.ShouldBe(TriageKind.Bug),
            () => blocked.BlockedBy.Select(blocker => blocker.IssueNumber).ShouldBe([31, 15]),
            () => blocked.BlockedBy.Select(blocker => blocker.Reason).ShouldBe(
                ["second by number, first named", "needs the retry policy"]));
    }
}
