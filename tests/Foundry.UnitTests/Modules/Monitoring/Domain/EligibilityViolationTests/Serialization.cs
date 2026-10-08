using System.Text.Json;

using Foundry.Modules.Monitoring.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.EligibilityViolationTests;

public sealed class Serialization
{
    private static readonly JsonSerializerOptions Options = new();

    [Fact]
    public void WhenCurrentJsonRoundTrips_PreservesRule()
    {
        // Arrange
        EligibilityViolation violation = EligibilityViolation.AllowDirectPushes();

        // Act
        string json = JsonSerializer.Serialize(violation, Options);
        EligibilityViolation? deserialized = JsonSerializer.Deserialize<EligibilityViolation>(json, Options);

        // Assert
        deserialized.ShouldNotBeNull();
        deserialized.Rule.ShouldBe(EligibilityViolation.AllowDirectPushesRule);
    }

    [Fact]
    public void WhenLegacyJsonWithDescriptionField_DeserializesSuccessfullyAndPreservesRule()
    {
        // Arrange — legacy rows were persisted with both "rule" and "description" fields.
        // The description field must be tolerated (not throw) and the rule preserved.
        const string legacyJson = """{"rule":"branch-protection:allow-direct-pushes","description":"Some old description."}""";

        // Act
        EligibilityViolation? deserialized = JsonSerializer.Deserialize<EligibilityViolation>(legacyJson, Options);

        // Assert
        deserialized.ShouldNotBeNull();
        deserialized.Rule.ShouldBe(EligibilityViolation.AllowDirectPushesRule);
    }

    [Fact]
    public void WhenPascalCaseJsonProvided_DeserializesRuleCorrectly()
    {
        // Arrange — PascalCase property names exercise OrdinalIgnoreCase matching in the converter.
        const string json = """{"Rule":"branch-protection:allow-direct-pushes","Description":"Some description."}""";

        // Act
        EligibilityViolation? deserialized = JsonSerializer.Deserialize<EligibilityViolation>(json, Options);

        // Assert
        deserialized.ShouldNotBeNull();
        deserialized.Rule.ShouldBe(EligibilityViolation.AllowDirectPushesRule);
    }

    [Fact]
    public void WhenRulePropertyMissing_ThrowsJsonException()
    {
        // Arrange — JSON object with no "rule" property at all.
        const string json = """{"description":"Some description without a rule."}""";

        // Act
        Action act = () => JsonSerializer.Deserialize<EligibilityViolation>(json, Options);

        // Assert
        Should.Throw<JsonException>(act).Message.ShouldContain("missing required 'rule'");
    }

    [Fact]
    public void WhenCurrentJsonSerialized_DoesNotIncludeDescriptionField()
    {
        // Arrange
        EligibilityViolation violation = EligibilityViolation.CannotPush("myorg/myrepo");

        // Act
        string json = JsonSerializer.Serialize(violation, Options);

        // Assert — no description key should be present in the serialized form
        using JsonDocument doc = JsonDocument.Parse(json);
        doc.RootElement.TryGetProperty("description", out _).ShouldBeFalse();
        doc.RootElement.TryGetProperty("Description", out _).ShouldBeFalse();
    }
}
