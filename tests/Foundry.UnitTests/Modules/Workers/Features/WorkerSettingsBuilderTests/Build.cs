using System.Text.Json;

using Foundry.Modules.Workers.Features;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.WorkerSettingsBuilderTests;

public sealed class Build
{
    private static readonly string[] BaseDenyList =
    [
        "Bash(git push --force:*)",
        "Bash(git push * main)",
        "Bash(git push * master)",
        "Bash(npm publish:*)",
        "Bash(npx -y:*)",
    ];

    [Fact]
    public void WhenSettingsIsNull_ReturnsJsonWithBaseDenyListOnly()
    {
        // Arrange

        // Act
        string json = WorkerSettingsBuilder.Build(null);

        // Assert
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;

        root.TryGetProperty("model", out _).ShouldBeFalse();
        root.TryGetProperty("hooks", out _).ShouldBeFalse();

        JsonElement deny = root
            .GetProperty("permissions")
            .GetProperty("deny");

        string[] rules = [.. deny.EnumerateArray().Select(x => x.GetString()!)];

        rules.ShouldBe(BaseDenyList);
    }

    [Fact]
    public void WhenSettingsIsEmpty_ReturnsJsonWithBaseDenyListOnly()
    {
        // Arrange
        WorkerSettingsOptions settings = new();

        // Act
        string json = WorkerSettingsBuilder.Build(settings);

        // Assert
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;

        root.TryGetProperty("model", out _).ShouldBeFalse();
        root.TryGetProperty("hooks", out _).ShouldBeFalse();

        JsonElement deny = root
            .GetProperty("permissions")
            .GetProperty("deny");

        string[] rules = [.. deny.EnumerateArray().Select(x => x.GetString()!)];

        rules.ShouldBe(BaseDenyList);
    }

    [Fact]
    public void WhenModelIsSet_IncludesModelInJson()
    {
        // Arrange
        WorkerSettingsOptions settings = new() { Model = "claude-opus-4-5" };

        // Act
        string json = WorkerSettingsBuilder.Build(settings);

        // Assert
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;

        root.GetProperty("model").GetString().ShouldBe("claude-opus-4-5");
    }

    [Fact]
    public void WhenModelIsNull_OmitsModelFromJson()
    {
        // Arrange
        WorkerSettingsOptions settings = new() { Model = null };

        // Act
        string json = WorkerSettingsBuilder.Build(settings);

        // Assert
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;

        root.TryGetProperty("model", out _).ShouldBeFalse();
    }

    [Fact]
    public void WhenAdditionalDenyRulesProvided_AppendsThemAfterBaseRules()
    {
        // Arrange
        WorkerSettingsOptions settings = new()
        {
            AdditionalDenyRules = ["Bash(rm -rf:*)", "Bash(curl:*)"],
        };

        // Act
        string json = WorkerSettingsBuilder.Build(settings);

        // Assert
        using JsonDocument doc = JsonDocument.Parse(json);

        JsonElement deny = doc.RootElement
            .GetProperty("permissions")
            .GetProperty("deny");

        string[] rules = [.. deny.EnumerateArray().Select(x => x.GetString()!)];

        rules.ShouldBe([..BaseDenyList, "Bash(rm -rf:*)", "Bash(curl:*)"]);
    }

    [Fact]
    public void WhenHooksConfigured_IncludesHooksSectionInJson()
    {
        // Arrange
        WorkerSettingsOptions settings = new()
        {
            Hooks = new Dictionary<string, List<HookGroup>>
            {
                ["PreToolUse"] =
                [
                    new HookGroup
                    {
                        Matcher = "Bash",
                        Hooks = [new HookEntry { Type = "command", Command = "echo pre-tool" }],
                    },
                ],
            },
        };

        // Act
        string json = WorkerSettingsBuilder.Build(settings);

        // Assert
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;

        root.TryGetProperty("hooks", out JsonElement hooksElement).ShouldBeTrue();

        hooksElement.TryGetProperty("PreToolUse", out JsonElement preToolUse).ShouldBeTrue();

        JsonElement firstGroup = preToolUse.EnumerateArray().First();

        firstGroup.GetProperty("matcher").GetString().ShouldBe("Bash");

        JsonElement firstHook = firstGroup
            .GetProperty("hooks")
            .EnumerateArray()
            .First();

        firstHook.ShouldSatisfyAllConditions(
            () => firstHook.GetProperty("type").GetString().ShouldBe("command"),
            () => firstHook.GetProperty("command").GetString().ShouldBe("echo pre-tool"));
    }

    [Fact]
    public void WhenAllOptionsSet_ProducesCorrectJson()
    {
        // Arrange
        WorkerSettingsOptions settings = new()
        {
            Model = "claude-sonnet-4-5",
            AdditionalDenyRules = ["Bash(rm -rf:*)"],
            Hooks = new Dictionary<string, List<HookGroup>>
            {
                ["PostToolUse"] =
                [
                    new HookGroup
                    {
                        Hooks = [new HookEntry { Command = "notify" }],
                    },
                ],
            },
        };

        // Act
        string json = WorkerSettingsBuilder.Build(settings);

        // Assert
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;

        root.ShouldSatisfyAllConditions(
            () => root.GetProperty("model").GetString().ShouldBe("claude-sonnet-4-5"),
            () => root.TryGetProperty("hooks", out _).ShouldBeTrue());

        JsonElement deny = root
            .GetProperty("permissions")
            .GetProperty("deny");

        string[] rules = [.. deny.EnumerateArray().Select(x => x.GetString()!)];

        rules.ShouldBe([..BaseDenyList, "Bash(rm -rf:*)"]);
    }
}
