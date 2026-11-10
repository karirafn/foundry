using System.Text.Json;

using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Domain.ValueObjects.FailureReasonTests;

public sealed class TurnLimitReached
{
    [Fact]
    public void CategoryToken_IsTurnLimit()
    {
        // Arrange
        FailureReason reason = new FailureReason.TurnLimitReached();

        // Act
        string token = reason.CategoryToken;

        // Assert
        token.ShouldBe("turn_limit");
        FailureCategory.FromToken(token).ShouldBe(FailureCategory.TurnLimit);
    }

    [Fact]
    public void Summary_TellsTheOperatorToSplitTheIssue()
    {
        // Arrange
        FailureReason reason = new FailureReason.TurnLimitReached();

        // Act
        string summary = reason.Summary;

        // Assert
        summary.ShouldBe("Reached the turn limit before finishing. Split the issue into smaller issues, then retry.");
    }

    [Fact]
    public void WhenRoundTripped_DeserializesAsTurnLimitReached()
    {
        // Arrange
        FailureReason original = new FailureReason.TurnLimitReached();

        // Act
        string json = JsonSerializer.Serialize(original);
        FailureReason? result = JsonSerializer.Deserialize<FailureReason>(json);

        // Assert
        json.ShouldContain(@"""$type"":""turn_limit""");
        result.ShouldBeOfType<FailureReason.TurnLimitReached>();
    }
}
