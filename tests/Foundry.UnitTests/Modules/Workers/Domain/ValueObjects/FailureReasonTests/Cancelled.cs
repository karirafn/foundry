using System.Text.Json;

using Foundry.Modules.Workers.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Domain.ValueObjects.FailureReasonTests;

public sealed class Cancelled
{
    [Fact]
    public void WhenTwoInstancesCreated_AreEqual()
    {
        // Arrange
        FailureReason.Cancelled a = new();
        FailureReason.Cancelled b = new();

        // Act

        // Assert
        a.ShouldBe(b);
    }

    [Fact]
    public void WhenAssignedToBaseType_IsCancelled()
    {
        // Arrange
        FailureReason reason = new FailureReason.Cancelled();

        // Act

        // Assert
        reason.ShouldBeOfType<FailureReason.Cancelled>();
    }

    [Fact]
    public void WhenSerialized_ContainsCancelledDiscriminator()
    {
        // Arrange
        FailureReason reason = new FailureReason.Cancelled();

        // Act
        string json = JsonSerializer.Serialize(reason);

        // Assert
        json.ShouldContain(@"""$type"":""cancelled""");
    }

    [Fact]
    public void WhenRoundTripped_DeserializesAsCancelled()
    {
        // Arrange
        FailureReason original = new FailureReason.Cancelled();

        // Act
        string json = JsonSerializer.Serialize(original);
        FailureReason? result = JsonSerializer.Deserialize<FailureReason>(json);

        // Assert
        result.ShouldBeOfType<FailureReason.Cancelled>();
    }
}
