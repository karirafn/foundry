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
        const string json = """{"Model":"opus","BlockCiCdEdits":true,"AdditionalDenyRules":null}""";

        // Act
        ClaudeCodeSettings settings = JsonSerializer.Deserialize<ClaudeCodeSettings>(json)!;

        // Assert
        settings.AdditionalDenyRules.ShouldBeEmpty();
    }

    [Fact]
    public void WhenRuleListIsNull_EqualsAndHashCodeMatchEmptyRuleInstance()
    {
        // Arrange
        const string json = """{"Model":"","BlockCiCdEdits":true,"AdditionalDenyRules":null}""";

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
        const string json = """{"Model":"opus","AdditionalDenyRules":[]}""";

        // Act
        ClaudeCodeSettings settings = JsonSerializer.Deserialize<ClaudeCodeSettings>(json)!;

        // Assert
        settings.BlockCiCdEdits.ShouldBeTrue();
    }

    [Fact]
    public void WhenRoundTripped_PreservesAllFields()
    {
        // Arrange
        ClaudeCodeSettings original = ClaudeCodeSettings
            .Create("opus", false, ["Bash(rm:*)"])
            .ValueOrThrow();

        // Act
        ClaudeCodeSettings roundTripped = JsonSerializer.Deserialize<ClaudeCodeSettings>(
            JsonSerializer.Serialize(original))!;

        // Assert
        roundTripped.ShouldBe(original);
    }
}
