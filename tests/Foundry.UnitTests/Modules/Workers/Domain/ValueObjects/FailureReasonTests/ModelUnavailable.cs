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

    [Theory]
    [InlineData(ImplementDemand.Routine, "Routine model")]
    [InlineData(ImplementDemand.Demanding, "Demanding model")]
    public void WhenTheRunHasADemand_SummaryNamesTheTierSetting(ImplementDemand demand, string setting)
    {
        // Arrange
        FailureReason reason = new FailureReason.ModelUnavailable("implement", demand);

        // Act
        string summary = reason.Summary;

        // Assert
        summary.ShouldBe(
            $"The selected model is unavailable to this account. Pick another model in the 'implement' worker role's '{setting}' setting");
    }

    [Fact]
    public void WhenRoundTripped_KeepsTheDemand()
    {
        // Arrange
        FailureReason original = new FailureReason.ModelUnavailable("implement", ImplementDemand.Routine);

        // Act
        FailureReason? result = JsonSerializer.Deserialize<FailureReason>(JsonSerializer.Serialize(original));

        // Assert
        result.ShouldBe(original);
    }

    [Fact]
    public void WhenStoredWithoutADemand_DeserializesWithNone()
    {
        // Arrange — the shape stored before failures named the tier.
        const string stored = """{"$type":"model_unavailable","Role":"implement"}""";

        // Act
        FailureReason? result = JsonSerializer.Deserialize<FailureReason>(stored);

        // Assert
        result.ShouldBe(new FailureReason.ModelUnavailable("implement"));
    }
}
