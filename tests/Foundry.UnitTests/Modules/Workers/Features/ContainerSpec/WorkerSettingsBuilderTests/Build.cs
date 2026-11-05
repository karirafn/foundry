using System.Text.Json;

using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Workers.Features.ContainerSpec;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.ContainerSpec.WorkerSettingsBuilderTests;

public sealed class Build
{
    private static ClaudeCodeSettingsSnapshot Snapshot(
        string model = "",
        bool blockCiCdEdits = true,
        params string[] additionalDenyRules) =>
        new(model, blockCiCdEdits, additionalDenyRules);

    private static string[] DenyRules(string json)
    {
        using JsonDocument doc = JsonDocument.Parse(json);

        return
        [
            .. doc.RootElement
                .GetProperty("permissions")
                .GetProperty("deny")
                .EnumerateArray()
                .Select(x => x.GetString()!),
        ];
    }

    [Fact]
    public void WhenBlockCiCdEditsTrueAndNothingElseSet_ReturnsJsonWithBaseAndCiCdRules()
    {
        // Arrange
        ClaudeCodeSettingsSnapshot snapshot = Snapshot();

        // Act
        string json = WorkerSettingsBuilder.Build(snapshot);

        // Assert
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;

        root.TryGetProperty("model", out _).ShouldBeFalse();
        root.TryGetProperty("hooks", out _).ShouldBeFalse();

        string[] rules = DenyRules(json);

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
        rules.ShouldContain("Edit(**/.github/workflows/**)");
        rules.ShouldContain("Edit(**/.gitlab-ci.yml)");
        rules.ShouldContain("Edit(**/Dockerfile)");
        rules.ShouldContain("Edit(**/docker-compose*.yml)");
        rules.ShouldContain("Edit(**/docker-compose.yaml)");
        rules.ShouldContain("Edit(**/compose.yml)");
        rules.ShouldContain("Edit(**/compose.yaml)");
    }

    [Fact]
    public void WhenModelIsSet_IncludesModelInJson()
    {
        // Arrange
        ClaudeCodeSettingsSnapshot snapshot = Snapshot(model: "claude-opus-4-5");

        // Act
        string json = WorkerSettingsBuilder.Build(snapshot);

        // Assert
        using JsonDocument doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("model").GetString().ShouldBe("claude-opus-4-5");
    }

    [Fact]
    public void WhenModelIsEmpty_OmitsModelFromJson()
    {
        // Arrange
        ClaudeCodeSettingsSnapshot snapshot = Snapshot(model: string.Empty);

        // Act
        string json = WorkerSettingsBuilder.Build(snapshot);

        // Assert
        using JsonDocument doc = JsonDocument.Parse(json);

        doc.RootElement.TryGetProperty("model", out _).ShouldBeFalse();
    }

    [Fact]
    public void WhenAdditionalDenyRulesProvided_AppendsThemAfterBaseAndCiCdRules()
    {
        // Arrange
        ClaudeCodeSettingsSnapshot snapshot = Snapshot(
            additionalDenyRules: ["Bash(rm -rf:*)", "Bash(curl:*)"]);

        // Act
        string json = WorkerSettingsBuilder.Build(snapshot);

        // Assert
        string[] rules = DenyRules(json);

        rules.Length.ShouldBe(19);
        rules[17].ShouldBe("Bash(rm -rf:*)");
        rules[18].ShouldBe("Bash(curl:*)");
    }

    [Fact]
    public void WhenCalled_NeverEmitsHooksSection()
    {
        // Arrange
        ClaudeCodeSettingsSnapshot snapshot = Snapshot(model: "claude-opus-4-5", additionalDenyRules: "Bash(rm -rf:*)");

        // Act
        string json = WorkerSettingsBuilder.Build(snapshot);

        // Assert
        using JsonDocument doc = JsonDocument.Parse(json);

        doc.RootElement.TryGetProperty("hooks", out _).ShouldBeFalse();
    }

    [Fact]
    public void WhenBlockCiCdEditsTrue_BaseDenyListContainsBranchDeletionRules()
    {
        // Arrange
        ClaudeCodeSettingsSnapshot snapshot = Snapshot();

        // Act
        string json = WorkerSettingsBuilder.Build(snapshot);

        // Assert
        string[] rules = DenyRules(json);

        rules.ShouldSatisfyAllConditions(
            () => rules.ShouldContain("Bash(git branch -D:*)"),
            () => rules.ShouldContain("Bash(git branch -d:*)"),
            () => rules.ShouldContain("Bash(git push --delete:*)"));
    }

    [Fact]
    public void WhenBlockCiCdEditsTrue_IncludesAllCiCdDenyRules()
    {
        // Arrange
        ClaudeCodeSettingsSnapshot snapshot = Snapshot(blockCiCdEdits: true);

        // Act
        string json = WorkerSettingsBuilder.Build(snapshot);

        // Assert
        string[] rules = DenyRules(json);

        rules.ShouldSatisfyAllConditions(
            () => rules.ShouldContain("Edit(**/.github/workflows/**)"),
            () => rules.ShouldContain("Edit(**/.gitlab-ci.yml)"),
            () => rules.ShouldContain("Edit(**/Dockerfile)"),
            () => rules.ShouldContain("Edit(**/docker-compose*.yml)"),
            () => rules.ShouldContain("Edit(**/docker-compose.yaml)"),
            () => rules.ShouldContain("Edit(**/compose.yml)"),
            () => rules.ShouldContain("Edit(**/compose.yaml)"));
    }

    [Fact]
    public void WhenBlockCiCdEditsTrue_CiCdRulesArePathGlobsMatchingAnyDepth()
    {
        // Arrange
        ClaudeCodeSettingsSnapshot snapshot = Snapshot(blockCiCdEdits: true);

        // Act
        string json = WorkerSettingsBuilder.Build(snapshot);

        // Assert — Claude Code skips Edit rules using the Bash-only ":*" prefix syntax as invalid.
        string[] editRules = [.. DenyRules(json).Where(rule => rule.StartsWith("Edit(", StringComparison.Ordinal))];

        editRules.Length.ShouldBe(7);
        editRules.ShouldAllBe(rule => rule.StartsWith("Edit(**/", StringComparison.Ordinal) && !rule.Contains(":*"));
    }

    [Fact]
    public void WhenBlockCiCdEditsFalse_CiCdRulesAbsentFromOutput()
    {
        // Arrange
        ClaudeCodeSettingsSnapshot snapshot = Snapshot(blockCiCdEdits: false);

        // Act
        string json = WorkerSettingsBuilder.Build(snapshot);

        // Assert
        string[] rules = DenyRules(json);

        rules.Length.ShouldBe(10);
        rules.ShouldSatisfyAllConditions(
            () => rules.ShouldNotContain("Edit(**/.github/workflows/**)"),
            () => rules.ShouldNotContain("Edit(**/.gitlab-ci.yml)"),
            () => rules.ShouldNotContain("Edit(**/Dockerfile)"),
            () => rules.ShouldNotContain("Edit(**/docker-compose*.yml)"),
            () => rules.ShouldNotContain("Edit(**/docker-compose.yaml)"),
            () => rules.ShouldNotContain("Edit(**/compose.yml)"),
            () => rules.ShouldNotContain("Edit(**/compose.yaml)"));
    }

    [Fact]
    public void WhenBlockCiCdEditsFalse_BaseRulesStillPresent()
    {
        // Arrange
        ClaudeCodeSettingsSnapshot snapshot = Snapshot(blockCiCdEdits: false);

        // Act
        string json = WorkerSettingsBuilder.Build(snapshot);

        // Assert
        string[] rules = DenyRules(json);

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
    public void WhenBlockCiCdEditsFalseAndAdditionalRulesSet_OrderIsBaseThenAdditional()
    {
        // Arrange
        ClaudeCodeSettingsSnapshot snapshot = Snapshot(
            blockCiCdEdits: false,
            additionalDenyRules: "Bash(rm -rf:*)");

        // Act
        string json = WorkerSettingsBuilder.Build(snapshot);

        // Assert
        string[] rules = DenyRules(json);

        rules.Length.ShouldBe(11);
        rules[9].ShouldBe("Bash(git push * :*)");
        rules[10].ShouldBe("Bash(rm -rf:*)");
    }

    [Fact]
    public void WhenAllSourcesPopulated_OrderIsBaseThenCiCdThenAdditional()
    {
        // Arrange
        ClaudeCodeSettingsSnapshot snapshot = Snapshot(additionalDenyRules: "Bash(rm -rf:*)");

        // Act
        string json = WorkerSettingsBuilder.Build(snapshot);

        // Assert
        string[] rules = DenyRules(json);

        rules.Length.ShouldBe(18);
        rules[0].ShouldBe("Bash(git push --force:*)");
        rules[9].ShouldBe("Bash(git push * :*)");
        rules[10].ShouldBe("Edit(**/.github/workflows/**)");
        rules[16].ShouldBe("Edit(**/compose.yaml)");
        rules[17].ShouldBe("Bash(rm -rf:*)");
    }

    [Fact]
    public void WhenModelAndRulesAllSet_ProducesCorrectJson()
    {
        // Arrange
        ClaudeCodeSettingsSnapshot snapshot = Snapshot(
            model: "claude-sonnet-4-5",
            additionalDenyRules: "Bash(rm -rf:*)");

        // Act
        string json = WorkerSettingsBuilder.Build(snapshot);

        // Assert
        using JsonDocument doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("model").GetString().ShouldBe("claude-sonnet-4-5");

        string[] rules = DenyRules(json);

        rules.Length.ShouldBe(18);
        rules.ShouldContain("Bash(git push --force:*)");
        rules.ShouldContain("Bash(npx -y:*)");
        rules[17].ShouldBe("Bash(rm -rf:*)");
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("claude-opus-4-5", false)]
    public void WhenCalled_SuppressesAiAttributionWithObjectForm(string model, bool blockCiCdEdits)
    {
        // Arrange
        ClaudeCodeSettingsSnapshot snapshot = Snapshot(model, blockCiCdEdits, "Bash(rm -rf:*)");

        // Act
        string json = WorkerSettingsBuilder.Build(snapshot);

        // Assert — the object form, not `false`, so a CLI older than v2.1.281 does not skip the whole file.
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement attribution = doc.RootElement.GetProperty("attribution");

        attribution.ValueKind.ShouldBe(JsonValueKind.Object);
        attribution.ShouldSatisfyAllConditions(
            () => attribution.GetProperty("commit").GetString().ShouldBe(string.Empty),
            () => attribution.GetProperty("pr").GetString().ShouldBe(string.Empty),
            () => attribution.GetProperty("sessionUrl").ValueKind.ShouldBe(JsonValueKind.False),
            () => attribution.EnumerateObject().Count().ShouldBe(3));
    }
}
