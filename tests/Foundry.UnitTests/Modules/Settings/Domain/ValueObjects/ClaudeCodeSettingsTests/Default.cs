using Foundry.Modules.Settings.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Domain.ValueObjects.ClaudeCodeSettingsTests;

public sealed class Default
{
    [Fact]
    public void HasNoAdditionalRules()
    {
        // Arrange & Act
        ClaudeCodeSettings settings = ClaudeCodeSettings.Default;

        // Assert
        settings.AdditionalDenyRules.ShouldBeEmpty();
    }
}
