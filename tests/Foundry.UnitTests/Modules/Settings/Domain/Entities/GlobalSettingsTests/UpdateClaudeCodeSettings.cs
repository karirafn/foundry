using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Modules.Settings.Domain.ValueObjects;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Domain.Entities.GlobalSettingsTests;

public sealed class UpdateClaudeCodeSettings
{
    private const string CustomDenyRule = "Bash(rm -rf:*)";

    private static ClaudeCodeSettings CreateCustom() =>
        new ClaudeCodeSettingsBuilder()
            .WithBlockCiCdEdits(false)
            .WithAdditionalDenyRules([CustomDenyRule])
            .Build()
            .Match(v => v, e => throw new InvalidOperationException(e.Message));

    [Fact]
    public void WhenCreated_ExposesDefaultClaudeCodeSettings()
    {
        // Arrange
        // Act
        GlobalSettings settings = GlobalSettings.Create();

        // Assert
        settings.ClaudeCodeSettings.ShouldBe(ClaudeCodeSettings.Default);
    }

    [Fact]
    public void WhenValueChanged_ReturnsTrueAndSetsValue()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        ClaudeCodeSettings custom = CreateCustom();

        // Act
        bool changed = settings.UpdateClaudeCodeSettings(custom);

        // Assert
        settings.ShouldSatisfyAllConditions(
            () => changed.ShouldBeTrue(),
            () => settings.ClaudeCodeSettings.ShouldBe(custom));
    }

    [Fact]
    public async Task WhenValueChanged_BumpsUpdatedAt()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        DateTimeOffset before = settings.UpdatedAt;
        await Task.Delay(5, TestContext.Current.CancellationToken);

        // Act
        settings.UpdateClaudeCodeSettings(CreateCustom());

        // Assert
        settings.UpdatedAt.ShouldBeGreaterThan(before);
    }

    [Fact]
    public void WhenValueEqualsCurrent_ReturnsFalseAndLeavesUpdatedAtUnchanged()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        DateTimeOffset before = settings.UpdatedAt;
        ClaudeCodeSettings equalValue = new ClaudeCodeSettingsBuilder()
            .Build()
            .Match(v => v, e => throw new InvalidOperationException(e.Message));

        // Act
        bool changed = settings.UpdateClaudeCodeSettings(equalValue);

        // Assert
        settings.ShouldSatisfyAllConditions(
            () => changed.ShouldBeFalse(),
            () => settings.UpdatedAt.ShouldBe(before));
    }
}
