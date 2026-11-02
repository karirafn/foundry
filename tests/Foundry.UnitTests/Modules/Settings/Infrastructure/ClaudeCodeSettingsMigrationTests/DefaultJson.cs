using System.Text.Json;

using Foundry.Modules.Settings.Domain.ValueObjects;
using Foundry.WebApi.Migrations;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Infrastructure.ClaudeCodeSettingsMigrationTests;

public sealed class DefaultJson
{
    [Fact]
    public void WhenDefaultSerialized_MatchesFrozenMigrationLiteral()
    {
        // Arrange
        // Act
        string json = JsonSerializer.Serialize(ClaudeCodeSettings.Default);

        // Assert
        json.ShouldBe(AddClaudeCodeSettings.DefaultClaudeCodeSettingsJson);
    }

    [Fact]
    public void WhenFrozenLiteralDeserialized_EqualsDefault()
    {
        // Arrange
        // Act
        ClaudeCodeSettings? settings = JsonSerializer.Deserialize<ClaudeCodeSettings>(
            AddClaudeCodeSettings.DefaultClaudeCodeSettingsJson);

        // Assert
        settings.ShouldBe(ClaudeCodeSettings.Default);
    }
}
