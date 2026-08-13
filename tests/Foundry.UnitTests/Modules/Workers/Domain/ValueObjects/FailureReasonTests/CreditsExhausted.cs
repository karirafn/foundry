using System.Text.Json;

using Foundry.Modules.Workers.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Domain.ValueObjects.FailureReasonTests;

public sealed class CreditsExhausted
{
    [Fact]
    public void WhenTwoInstancesCreated_AreEqual()
    {
        // Arrange
        FailureReason.CreditsExhausted a = new();
        FailureReason.CreditsExhausted b = new();

        // Act

        // Assert
        a.ShouldBe(b);
    }

    [Fact]
    public void WhenAssignedToBaseType_IsCreditsExhausted()
    {
        // Arrange
        FailureReason reason = new FailureReason.CreditsExhausted();

        // Act

        // Assert
        reason.ShouldBeOfType<FailureReason.CreditsExhausted>();
    }

    [Fact]
    public void WhenSerialized_ContainsCreditsExhaustedDiscriminator()
    {
        // Arrange
        FailureReason reason = new FailureReason.CreditsExhausted();

        // Act
        string json = JsonSerializer.Serialize(reason);

        // Assert
        json.ShouldContain(@"""$type"":""credits_exhausted""");
    }

    [Fact]
    public void WhenRoundTripped_DeserializesAsCreditsExhausted()
    {
        // Arrange
        FailureReason original = new FailureReason.CreditsExhausted();

        // Act
        string json = JsonSerializer.Serialize(original);
        FailureReason? result = JsonSerializer.Deserialize<FailureReason>(json);

        // Assert
        result.ShouldBeOfType<FailureReason.CreditsExhausted>();
    }
}
