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
        const string json = """{"BlockCiCdEdits":true,"AdditionalDenyRules":null}""";

        // Act
        ClaudeCodeSettings settings = JsonSerializer.Deserialize<ClaudeCodeSettings>(json)!;

        // Assert
        settings.AdditionalDenyRules.ShouldBeEmpty();
    }

    [Fact]
    public void WhenRuleListIsNull_EqualsAndHashCodeMatchEmptyRuleInstance()
    {
        // Arrange
        const string json = """{"BlockCiCdEdits":true,"AdditionalDenyRules":null}""";

        // Act
        ClaudeCodeSettings settings = JsonSerializer.Deserialize<ClaudeCodeSettings>(json)!;

        // Assert
        settings.ShouldSatisfyAllConditions(
            () => settings.ShouldBe(ClaudeCodeSettings.Default),
            () => settings.GetHashCode().ShouldBe(ClaudeCodeSettings.Default.GetHashCode()));
    }

    [Fact]
    public void WhenBlockCiCdEditsIsMissing_FailsClosedWithProtectionOn()
    {
        // Arrange
        const string json = """{"AdditionalDenyRules":[]}""";

        // Act
        ClaudeCodeSettings settings = JsonSerializer.Deserialize<ClaudeCodeSettings>(json)!;

        // Assert
        settings.BlockCiCdEdits.ShouldBeTrue();
    }

    [Fact]
    public void WhenLegacyJsonCarriesAGlobalModel_IgnoresIt()
    {
        // Arrange — rows written before the model moved to the worker roles (ADR 0095)
        const string json = """{"Model":"opus","BlockCiCdEdits":false,"AdditionalDenyRules":["Bash(rm:*)"]}""";

        // Act
        ClaudeCodeSettings settings = JsonSerializer.Deserialize<ClaudeCodeSettings>(json)!;

        // Assert
        settings.ShouldBe(ClaudeCodeSettings.Create(false, ["Bash(rm:*)"]).ValueOrThrow());
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
            .Create(false, ["Bash(rm:*)"])
            .ValueOrThrow();

        // Act
        ClaudeCodeSettings roundTripped = JsonSerializer.Deserialize<ClaudeCodeSettings>(
            JsonSerializer.Serialize(original))!;

        // Assert
        roundTripped.ShouldBe(original);
    }
}
