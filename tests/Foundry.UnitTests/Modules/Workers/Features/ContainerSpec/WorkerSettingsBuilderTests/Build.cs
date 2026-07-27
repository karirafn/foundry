using System.Text.Json;

using Foundry.Modules.Workers.Features;
using Foundry.Modules.Workers.Features.ContainerSpec;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.ContainerSpec.WorkerSettingsBuilderTests;

public sealed class Build
{
    [Fact]
    public void WhenSettingsIsEmpty_ReturnsJsonWithBaseAndDefaultCiCdRules()
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

        rules.Length.ShouldBe(17);
        rules.ShouldContain("Bash(git push --force:*)");
        rules.ShouldContain("Bash(git push * main)");
        rules.ShouldContain("Bash(git push * master)");
        rules.ShouldContain("Bash(npm publish:*)");
        rules.ShouldContain("Bash(npx -y:*)");
        rules.ShouldContain("Bash(git branch -D:*)");
        rules.ShouldContain("Bash(git branch -d:*)");
        rules.ShouldContain("Bash(git push --delete:*)");
        rules.ShouldContain("Bash(git push * HEAD:*)");
        rules.ShouldContain("Bash(git push * :*)");
        rules.ShouldContain("Edit(.github/workflows/**:*)");
        rules.ShouldContain("Edit(.gitlab-ci.yml:*)");
        rules.ShouldContain("Edit(Dockerfile:*)");
        rules.ShouldContain("Edit(docker-compose*.yml:*)");
        rules.ShouldContain("Edit(docker-compose.yaml:*)");
        rules.ShouldContain("Edit(compose.yml:*)");
        rules.ShouldContain("Edit(compose.yaml:*)");
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
    public void WhenAdditionalDenyRulesProvided_AppendsThemAfterBaseAndCiCdRules()
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

        rules.Length.ShouldBe(19);
        rules[17].ShouldBe("Bash(rm -rf:*)");
        rules[18].ShouldBe("Bash(curl:*)");
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
    public void WhenSettingsIsEmpty_BaseDenyListContainsBranchDeletionRules()
    {
        // Arrange
        WorkerSettingsOptions settings = new();

        // Act
        string json = WorkerSettingsBuilder.Build(settings);

        // Assert
        using JsonDocument doc = JsonDocument.Parse(json);

        JsonElement deny = doc.RootElement
            .GetProperty("permissions")
            .GetProperty("deny");

        string[] rules = [.. deny.EnumerateArray().Select(x => x.GetString()!)];

        rules.ShouldSatisfyAllConditions(
            () => rules.ShouldContain("Bash(git branch -D:*)"),
            () => rules.ShouldContain("Bash(git branch -d:*)"),
            () => rules.ShouldContain("Bash(git push --delete:*)"));
    }

    [Fact]
    public void WhenSettingsIsEmpty_IncludesDefaultCiCdDenyRules()
    {
        // Arrange
        WorkerSettingsOptions settings = new();

        // Act
        string json = WorkerSettingsBuilder.Build(settings);

        // Assert
        using JsonDocument doc = JsonDocument.Parse(json);

        JsonElement deny = doc.RootElement
            .GetProperty("permissions")
            .GetProperty("deny");

        string[] rules = [.. deny.EnumerateArray().Select(x => x.GetString()!)];

        rules.ShouldSatisfyAllConditions(
            () => rules.ShouldContain("Edit(.github/workflows/**:*)"),
            () => rules.ShouldContain("Edit(.gitlab-ci.yml:*)"),
            () => rules.ShouldContain("Edit(Dockerfile:*)"),
            () => rules.ShouldContain("Edit(docker-compose*.yml:*)"),
            () => rules.ShouldContain("Edit(docker-compose.yaml:*)"),
            () => rules.ShouldContain("Edit(compose.yml:*)"),
            () => rules.ShouldContain("Edit(compose.yaml:*)"));
    }

    [Fact]
    public void WhenCiCdDenyRulesCleared_CiCdRulesAbsentFromOutput()
    {
        // Arrange
        WorkerSettingsOptions settings = new()
        {
            CiCdDenyRules = [],
        };

        // Act
        string json = WorkerSettingsBuilder.Build(settings);

        // Assert
        using JsonDocument doc = JsonDocument.Parse(json);

        JsonElement deny = doc.RootElement
            .GetProperty("permissions")
            .GetProperty("deny");

        string[] rules = [.. deny.EnumerateArray().Select(x => x.GetString()!)];

        rules.Length.ShouldBe(10);
        rules.ShouldSatisfyAllConditions(
            () => rules.ShouldNotContain("Edit(.github/workflows/**:*)"),
            () => rules.ShouldNotContain("Edit(.gitlab-ci.yml:*)"),
            () => rules.ShouldNotContain("Edit(Dockerfile:*)"),
            () => rules.ShouldNotContain("Edit(docker-compose*.yml:*)"));
    }

    [Fact]
    public void WhenCiCdDenyRulesCleared_BaseRulesStillPresent()
    {
        // Arrange
        WorkerSettingsOptions settings = new()
        {
            CiCdDenyRules = [],
        };

        // Act
        string json = WorkerSettingsBuilder.Build(settings);

        // Assert
        using JsonDocument doc = JsonDocument.Parse(json);

        JsonElement deny = doc.RootElement
            .GetProperty("permissions")
            .GetProperty("deny");

        string[] rules = [.. deny.EnumerateArray().Select(x => x.GetString()!)];

        rules.ShouldSatisfyAllConditions(
            () => rules.ShouldContain("Bash(git push --force:*)"),
            () => rules.ShouldContain("Bash(git push * main)"),
            () => rules.ShouldContain("Bash(git push * master)"),
            () => rules.ShouldContain("Bash(npm publish:*)"),
            () => rules.ShouldContain("Bash(npx -y:*)"),
            () => rules.ShouldContain("Bash(git branch -D:*)"),
            () => rules.ShouldContain("Bash(git branch -d:*)"),
            () => rules.ShouldContain("Bash(git push --delete:*)"),
            () => rules.ShouldContain("Bash(git push * HEAD:*)"),
            () => rules.ShouldContain("Bash(git push * :*)"));
    }

    [Fact]
    public void WhenAllThreeDenySourcesPopulated_OrderIsBaseThenCiCdThenAdditional()
    {
        // Arrange
        WorkerSettingsOptions settings = new()
        {
            CiCdDenyRules = ["Edit(.github/workflows/**:*)"],
            AdditionalDenyRules = ["Bash(rm -rf:*)"],
        };

        // Act
        string json = WorkerSettingsBuilder.Build(settings);

        // Assert
        using JsonDocument doc = JsonDocument.Parse(json);

        JsonElement deny = doc.RootElement
            .GetProperty("permissions")
            .GetProperty("deny");

        string[] rules = [.. deny.EnumerateArray().Select(x => x.GetString()!)];

        // 10 base + 1 CI/CD + 1 additional = 12
        rules.Length.ShouldBe(12);

        // Base rules come first (indices 0-9)
        rules[0].ShouldBe("Bash(git push --force:*)");
        rules[9].ShouldBe("Bash(git push * :*)");

        // CI/CD rule follows base (index 10)
        rules[10].ShouldBe("Edit(.github/workflows/**:*)");

        // Additional rule comes last (index 11)
        rules[11].ShouldBe("Bash(rm -rf:*)");
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

        rules.Length.ShouldBe(18);
        rules.ShouldContain("Bash(git push --force:*)");
        rules.ShouldContain("Bash(npx -y:*)");
        rules[17].ShouldBe("Bash(rm -rf:*)");
    }
}
