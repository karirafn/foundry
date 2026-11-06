using System.Text.Json;
using System.Text.Json.Nodes;

using Foundry.Modules.Settings.Domain.ValueObjects;
using Foundry.WebApi.Migrations;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Infrastructure.ClaudeCodeSettingsMigrationTests;

public sealed class DefaultJson
{
    [Fact]
    public void WhenFrozenLiteralHasItsModelRemoved_MatchesDefaultSerialization()
    {
        // Arrange — AddWorkerRoleModelAndRemoveGlobalModel removes Model from the stored JSON (ADR 0095)
        JsonObject migrated = JsonNode.Parse(AddClaudeCodeSettings.DefaultClaudeCodeSettingsJson)!.AsObject();
        migrated.Remove("Model");

        // Act
        string json = JsonSerializer.Serialize(ClaudeCodeSettings.Default);

        // Assert
        json.ShouldBe(migrated.ToJsonString());
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
