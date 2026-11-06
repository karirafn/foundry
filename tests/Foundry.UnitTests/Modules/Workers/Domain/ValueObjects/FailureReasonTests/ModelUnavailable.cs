using System.Text.Json;

using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Domain.ValueObjects.FailureReasonTests;

public sealed class ModelUnavailable
{
    [Fact]
    public void CategoryToken_IsModelUnavailable()
    {
        // Arrange
        FailureReason reason = new FailureReason.ModelUnavailable("implement");

        // Act
        string token = reason.CategoryToken;

        // Assert
        token.ShouldBe("model_unavailable");
        FailureCategory.FromToken(token).ShouldBe(FailureCategory.ModelUnavailable);
    }

    [Fact]
    public void Summary_PointsAtTheRolesModelSetting()
    {
        // Arrange
        FailureReason reason = new FailureReason.ModelUnavailable("implement");

        // Act
        string summary = reason.Summary;

        // Assert
        summary.ShouldSatisfyAllConditions(
            () => summary.ShouldContain("model"),
            () => summary.ShouldContain("'implement' worker role"));
    }

    [Fact]
    public void WhenRoundTripped_KeepsTheRole()
    {
        // Arrange
        FailureReason original = new FailureReason.ModelUnavailable("implement");

        // Act
        string json = JsonSerializer.Serialize(original);
        FailureReason? result = JsonSerializer.Deserialize<FailureReason>(json);

        // Assert
        json.ShouldContain(@"""$type"":""model_unavailable""");
        result.ShouldBe(original);
    }
}
