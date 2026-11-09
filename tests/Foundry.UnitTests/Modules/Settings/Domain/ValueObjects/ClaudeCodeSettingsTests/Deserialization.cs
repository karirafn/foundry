using System.Text.Json;

using Foundry.Modules.Settings.Domain.ValueObjects;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Domain.ValueObjects.ClaudeCodeSettingsTests;

public sealed class Deserialization
{
    [Fact]
    public void WhenRuleListIsNull_DeserializesToEmptyRules()
    {
        // Arrange
        const string json = """{"AdditionalDenyRules":null}""";

        // Act
        ClaudeCodeSettings settings = JsonSerializer.Deserialize<ClaudeCodeSettings>(json)!;

        // Assert
        settings.AdditionalDenyRules.ShouldBeEmpty();
    }

    [Fact]
    public void WhenRuleListIsNull_EqualsAndHashCodeMatchEmptyRuleInstance()
    {
        // Arrange
        const string json = """{"AdditionalDenyRules":null}""";

        // Act
        ClaudeCodeSettings settings = JsonSerializer.Deserialize<ClaudeCodeSettings>(json)!;

        // Assert
        settings.ShouldSatisfyAllConditions(
            () => settings.ShouldBe(ClaudeCodeSettings.Default),
            () => settings.GetHashCode().ShouldBe(ClaudeCodeSettings.Default.GetHashCode()));
    }

    [Fact]
    public void WhenLegacyJsonCarriesBlockCiCdEdits_IgnoresIt()
    {
        // Arrange — rows written before the CI/CD edit-blocking setting was removed (ADR 0100)
        const string json = """{"BlockCiCdEdits":true,"AdditionalDenyRules":["Bash(rm:*)"]}""";

        // Act
        ClaudeCodeSettings settings = JsonSerializer.Deserialize<ClaudeCodeSettings>(json)!;

        // Assert
        settings.ShouldBe(ClaudeCodeSettings.Create(["Bash(rm:*)"]).ValueOrThrow());
    }

    [Fact]
    public void WhenLegacyJsonIsReserialized_WritesNoBlockCiCdEdits()
    {
        // Arrange
        const string json = """{"BlockCiCdEdits":false,"AdditionalDenyRules":[]}""";
        ClaudeCodeSettings settings = JsonSerializer.Deserialize<ClaudeCodeSettings>(json)!;

        // Act
        string reserialized = JsonSerializer.Serialize(settings);

        // Assert
        reserialized.ShouldNotContain("BlockCiCdEdits");
    }

    [Fact]
    public void WhenLegacyJsonCarriesAGlobalModel_IgnoresIt()
    {
        // Arrange — rows written before the model moved to the worker roles (ADR 0095)
        const string json = """{"Model":"opus","AdditionalDenyRules":["Bash(rm:*)"]}""";

        // Act
        ClaudeCodeSettings settings = JsonSerializer.Deserialize<ClaudeCodeSettings>(json)!;

        // Assert
        settings.ShouldBe(ClaudeCodeSettings.Create(["Bash(rm:*)"]).ValueOrThrow());
    }

    [Fact]
    public void WhenRoundTripped_SerializesNoModel()
    {
        // Arrange & Act
        string json = JsonSerializer.Serialize(ClaudeCodeSettings.Default);

        // Assert
        json.ShouldNotContain("Model");
    }

    [Fact]
    public void WhenRoundTripped_PreservesAllFields()
    {
        // Arrange
        ClaudeCodeSettings original = ClaudeCodeSettings
            .Create(["Bash(rm:*)"])
            .ValueOrThrow();

        // Act
        ClaudeCodeSettings roundTripped = JsonSerializer.Deserialize<ClaudeCodeSettings>(
            JsonSerializer.Serialize(original))!;

        // Assert
        roundTripped.ShouldBe(original);
    }
}
