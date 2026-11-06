using Foundry.Modules.Settings.Domain.ValueObjects;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Domain.ValueObjects.ClaudeCodeSettingsTests;

public sealed class ValueEquality
{
    [Fact]
    public void WhenAllFieldsMatch_InstancesAreEqualWithEqualHashCodes()
    {
        // Arrange
        ClaudeCodeSettings first = new ClaudeCodeSettingsBuilder()
            .WithAdditionalDenyRules(["Bash(rm:*)", "Edit(a)"])
            .Build()
            .ValueOrThrow();
        ClaudeCodeSettings second = new ClaudeCodeSettingsBuilder()
            .WithAdditionalDenyRules(["Bash(rm:*)", "Edit(a)"])
            .Build()
            .ValueOrThrow();

        // Act & Assert
        first.ShouldSatisfyAllConditions(
            () => first.ShouldBe(second),
            () => first.GetHashCode().ShouldBe(second.GetHashCode()));
    }

    [Fact]
    public void WhenDenyRulesDiffer_InstancesAreNotEqual()
    {
        // Arrange
        ClaudeCodeSettings first = new ClaudeCodeSettingsBuilder()
            .WithAdditionalDenyRules(["Bash(rm:*)"])
            .Build()
            .ValueOrThrow();
        ClaudeCodeSettings second = new ClaudeCodeSettingsBuilder()
            .WithAdditionalDenyRules(["Bash(ls:*)"])
            .Build()
            .ValueOrThrow();

        // Act & Assert
        first.ShouldNotBe(second);
    }

    [Fact]
    public void WhenBlockCiCdEditsDiffers_InstancesAreNotEqual()
    {
        // Arrange
        ClaudeCodeSettings first = new ClaudeCodeSettingsBuilder().WithBlockCiCdEdits(true).Build().ValueOrThrow();
        ClaudeCodeSettings second = new ClaudeCodeSettingsBuilder().WithBlockCiCdEdits(false).Build().ValueOrThrow();

        // Act & Assert
        first.ShouldNotBe(second);
    }
}
