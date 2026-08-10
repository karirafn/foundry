using System.Text.Json;

using Foundry.Modules.Workers.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Domain.ValueObjects.FailureReasonTests;

public sealed class TransientApiError
{
    [Fact]
    public void WhenTwoInstancesCreated_AreEqual()
    {
        // Arrange
        FailureReason.TransientApiError a = new();
        FailureReason.TransientApiError b = new();

        // Act

        // Assert
        a.ShouldBe(b);
    }

    [Fact]
    public void WhenAssignedToBaseType_IsTransientApiError()
    {
        // Arrange
        FailureReason reason = new FailureReason.TransientApiError();

        // Act

        // Assert
        reason.ShouldBeOfType<FailureReason.TransientApiError>();
    }

    [Fact]
    public void WhenSerialized_ContainsTransientApiErrorDiscriminator()
    {
        // Arrange
        FailureReason reason = new FailureReason.TransientApiError();

        // Act
        string json = JsonSerializer.Serialize(reason);

        // Assert
        json.ShouldContain(@"""$type"":""transient_api_error""");
    }

    [Fact]
    public void WhenRoundTripped_DeserializesAsTransientApiError()
    {
        // Arrange
        FailureReason original = new FailureReason.TransientApiError();

        // Act
        string json = JsonSerializer.Serialize(original);
        FailureReason? result = JsonSerializer.Deserialize<FailureReason>(json);

        // Assert
        result.ShouldBeOfType<FailureReason.TransientApiError>();
    }
}
