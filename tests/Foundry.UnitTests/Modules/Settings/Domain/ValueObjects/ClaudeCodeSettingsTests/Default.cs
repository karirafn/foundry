using Foundry.Modules.Settings.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Domain.ValueObjects.ClaudeCodeSettingsTests;

public sealed class Default
{
    [Fact]
    public void HasNoModelBlocksCiCdEditsAndHasNoAdditionalRules()
    {
        // Arrange & Act
        ClaudeCodeSettings settings = ClaudeCodeSettings.Default;

        // Assert
        settings.ShouldSatisfyAllConditions(
            () => settings.Model.ShouldBeEmpty(),
            () => settings.BlockCiCdEdits.ShouldBeTrue(),
            () => settings.AdditionalDenyRules.ShouldBeEmpty());
    }
}
