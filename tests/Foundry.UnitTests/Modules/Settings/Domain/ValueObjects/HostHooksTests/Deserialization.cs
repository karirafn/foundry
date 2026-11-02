using System.Text.Json;

using Foundry.Modules.Settings.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Domain.ValueObjects.HostHooksTests;

public sealed class Deserialization
{
    [Fact]
    public void WhenRoundTripped_PreservesPathAndExclusions()
    {
        // Arrange
        HostHooks original = HostHooks.Create("/home/user/.claude/settings.json", ["notify", "lint"])
            .Match(v => v, e => throw new InvalidOperationException(e.Message));

        // Act
        HostHooks roundTripped = JsonSerializer.Deserialize<HostHooks>(JsonSerializer.Serialize(original))!;

        // Assert
        roundTripped.ShouldBe(original);
    }

    [Fact]
    public void WhenExclusionListIsNull_DeserializesToDefault()
    {
        // Arrange
        const string json = """{"HostSettingsPath":"","ExcludedCommands":null}""";

        // Act
        HostHooks hostHooks = JsonSerializer.Deserialize<HostHooks>(json)!;

        // Assert
        hostHooks.ShouldSatisfyAllConditions(
            () => hostHooks.ShouldBe(HostHooks.Default),
            () => hostHooks.GetHashCode().ShouldBe(HostHooks.Default.GetHashCode()));
    }
}
