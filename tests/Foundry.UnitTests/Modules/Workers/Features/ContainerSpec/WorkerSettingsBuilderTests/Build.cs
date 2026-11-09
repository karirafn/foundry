using System.Text.Json;

using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Workers.Features.ContainerSpec;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.ContainerSpec.WorkerSettingsBuilderTests;

public sealed class Build
{
    private static RunInput Snapshot(
        string model = "",
        params string[] additionalDenyRules) =>
        new(new ClaudeCodeSettingsSnapshot(additionalDenyRules), model);

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
    public void WhenNothingElseSet_ReturnsJsonWithBaseRulesOnly()
    {
        // Arrange
        RunInput snapshot = Snapshot();

        // Act
        string json = WorkerSettingsBuilder.Build(snapshot.ClaudeCode, snapshot.Model);

        // Assert
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;

        root.TryGetProperty("model", out _).ShouldBeFalse();
        root.TryGetProperty("hooks", out _).ShouldBeFalse();

        string[] rules = DenyRules(json);

        rules.ShouldBe(
        [
            "Bash(git push --force:*)",
            "Bash(git push * main)",
            "Bash(git push * master)",
            "Bash(npm publish:*)",
            "Bash(npx -y:*)",
            "Bash(git branch -D:*)",
            "Bash(git branch -d:*)",
            "Bash(git push --delete:*)",
            "Bash(git push * HEAD:*)",
            "Bash(git push * :*)",
        ]);
    }

    [Fact]
    public void WhenCalled_InjectsNoCiCdEditRules()
    {
        // Arrange
        RunInput snapshot = Snapshot();

        // Act
        string json = WorkerSettingsBuilder.Build(snapshot.ClaudeCode, snapshot.Model);

        // Assert — CI/CD files are published like any other file; pull request review is the gate (ADR 0100).
        DenyRules(json).ShouldNotContain(rule => rule.StartsWith("Edit(", StringComparison.Ordinal));
    }

    [Fact]
    public void WhenModelIsSet_IncludesModelInJson()
    {
        // Arrange
        RunInput snapshot = Snapshot(model: "claude-opus-4-5");

        // Act
        string json = WorkerSettingsBuilder.Build(snapshot.ClaudeCode, snapshot.Model);

        // Assert
        using JsonDocument doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("model").GetString().ShouldBe("claude-opus-4-5");
    }

    [Fact]
    public void WhenModelIsAnAlias_PassesItThrough()
    {
        // Arrange
        RunInput snapshot = Snapshot(model: "opus[1m]");

        // Act
        string json = WorkerSettingsBuilder.Build(snapshot.ClaudeCode, snapshot.Model);

        // Assert
        using JsonDocument doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("model").GetString().ShouldBe("opus[1m]");
    }

    [Fact]
    public void WhenModelIsEmpty_OmitsModelFromJson()
    {
        // Arrange
        RunInput snapshot = Snapshot(model: string.Empty);

        // Act
        string json = WorkerSettingsBuilder.Build(snapshot.ClaudeCode, snapshot.Model);

        // Assert
        using JsonDocument doc = JsonDocument.Parse(json);

        doc.RootElement.TryGetProperty("model", out _).ShouldBeFalse();
    }

    [Fact]
    public void WhenAdditionalDenyRulesProvided_AppendsThemAfterBaseRules()
    {
        // Arrange
        RunInput snapshot = Snapshot(
            additionalDenyRules: ["Bash(rm -rf:*)", "Bash(curl:*)"]);

        // Act
        string json = WorkerSettingsBuilder.Build(snapshot.ClaudeCode, snapshot.Model);

        // Assert
        string[] rules = DenyRules(json);

        rules.Length.ShouldBe(12);
        rules[9].ShouldBe("Bash(git push * :*)");
        rules[10].ShouldBe("Bash(rm -rf:*)");
        rules[11].ShouldBe("Bash(curl:*)");
    }

    [Fact]
    public void WhenOperatorListsCiCdGlobsInAdditionalRules_KeepsThem()
    {
        // Arrange
        RunInput snapshot = Snapshot(additionalDenyRules: "Edit(**/.github/workflows/**)");

        // Act
        string json = WorkerSettingsBuilder.Build(snapshot.ClaudeCode, snapshot.Model);

        // Assert
        DenyRules(json)[^1].ShouldBe("Edit(**/.github/workflows/**)");
    }

    [Fact]
    public void WhenCalled_NeverEmitsHooksSection()
    {
        // Arrange
        RunInput snapshot = Snapshot(model: "claude-opus-4-5", additionalDenyRules: "Bash(rm -rf:*)");

        // Act
        string json = WorkerSettingsBuilder.Build(snapshot.ClaudeCode, snapshot.Model);

        // Assert
        using JsonDocument doc = JsonDocument.Parse(json);

        doc.RootElement.TryGetProperty("hooks", out _).ShouldBeFalse();
    }

    [Fact]
    public void WhenCalled_BaseDenyListContainsBranchDeletionRules()
    {
        // Arrange
        RunInput snapshot = Snapshot();

        // Act
        string json = WorkerSettingsBuilder.Build(snapshot.ClaudeCode, snapshot.Model);

        // Assert
        string[] rules = DenyRules(json);

        rules.ShouldSatisfyAllConditions(
            () => rules.ShouldContain("Bash(git branch -D:*)"),
            () => rules.ShouldContain("Bash(git branch -d:*)"),
            () => rules.ShouldContain("Bash(git push --delete:*)"));
    }

    [Fact]
    public void WhenModelAndRulesAllSet_ProducesCorrectJson()
    {
        // Arrange
        RunInput snapshot = Snapshot(
            model: "claude-sonnet-4-5",
            additionalDenyRules: "Bash(rm -rf:*)");

        // Act
        string json = WorkerSettingsBuilder.Build(snapshot.ClaudeCode, snapshot.Model);

        // Assert
        using JsonDocument doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("model").GetString().ShouldBe("claude-sonnet-4-5");

        string[] rules = DenyRules(json);

        rules.Length.ShouldBe(11);
        rules.ShouldContain("Bash(git push --force:*)");
        rules.ShouldContain("Bash(npx -y:*)");
        rules[10].ShouldBe("Bash(rm -rf:*)");
    }

    [Theory]
    [InlineData("")]
    [InlineData("claude-opus-4-5")]
    public void WhenCalled_SuppressesAiAttributionWithObjectForm(string model)
    {
        // Arrange
        RunInput snapshot = Snapshot(model, "Bash(rm -rf:*)");

        // Act
        string json = WorkerSettingsBuilder.Build(snapshot.ClaudeCode, snapshot.Model);

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

    private sealed record RunInput(ClaudeCodeSettingsSnapshot ClaudeCode, string Model);
}
