using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Roles;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Roles.WorkerRoleCatalogReaderTests;

public sealed class Parse
{
    private static Dictionary<string, string> ValidFiles() => new(StringComparer.Ordinal)
    {
        ["implement/.claude-plugin/plugin.json"] = """{"name":"implement","description":"Implements an issue."}""",
        ["implement/prompts/system.md"] = "You are implementing issue #{issueNumber}.\r\n\r\n{issueContent}\r\n",
        ["implement/prompts/task.md"] = "Implement issue #{issueNumber}.\n",
        ["shared/.claude-plugin/plugin.json"] = """{"name":"shared","description":"Loaded for every role."}""",
    };

    [Fact]
    public void WhenEveryRoleAndTheSharedPluginArePresent_ReadsPromptsAndPurpose()
    {
        // Act
        Result<WorkerRoleCatalogContent> result = WorkerRoleCatalogReader.Parse(ValidFiles());

        // Assert
        WorkerRoleCatalogContent catalog = result.ShouldBeOfType<Result<WorkerRoleCatalogContent>.Success>().Value;
        WorkerRoleDefinition implement = catalog.Find(WorkerRole.Implement).ShouldNotBeNull();
        implement.ShouldSatisfyAllConditions(
            () => implement.Plugin.Description.ShouldBe("Implements an issue."),
            () => implement.SystemPromptTemplate.ShouldBe("You are implementing issue #{issueNumber}.\n\n{issueContent}"),
            () => implement.TaskPromptTemplate.ShouldBe("Implement issue #{issueNumber}."),
            () => implement.VerdictSchema.ShouldBeNull(),
            () => catalog.Shared.Description.ShouldBe("Loaded for every role."));
    }

    [Fact]
    public void WhenARoleDefinedInCodeHasNoFolder_Fails()
    {
        // Arrange
        Dictionary<string, string> files = ValidFiles();
        files.Remove("implement/.claude-plugin/plugin.json");
        files.Remove("implement/prompts/system.md");
        files.Remove("implement/prompts/task.md");

        // Act
        Result<WorkerRoleCatalogContent> result = WorkerRoleCatalogReader.Parse(files);

        // Assert
        Error error = result.ShouldBeOfType<Result<WorkerRoleCatalogContent>.Failure>().Error;
        error.ShouldSatisfyAllConditions(
            () => error.Code.ShouldBe(WorkerRoleCatalogReader.InvalidCatalogCode),
            () => error.Message.ShouldContain("roles/implement/"));
    }

    [Fact]
    public void WhenTheSharedPluginIsMissing_Fails()
    {
        // Arrange
        Dictionary<string, string> files = ValidFiles();
        files.Remove("shared/.claude-plugin/plugin.json");

        // Act
        Result<WorkerRoleCatalogContent> result = WorkerRoleCatalogReader.Parse(files);

        // Assert
        result.ShouldBeOfType<Result<WorkerRoleCatalogContent>.Failure>().Error.Message.ShouldContain("roles/shared/");
    }

    [Theory]
    [InlineData("implement/prompts/system.md")]
    [InlineData("implement/prompts/task.md")]
    public void WhenARolePromptIsMissing_FailsNamingTheFile(string path)
    {
        // Arrange
        Dictionary<string, string> files = ValidFiles();
        files.Remove(path);

        // Act
        Result<WorkerRoleCatalogContent> result = WorkerRoleCatalogReader.Parse(files);

        // Assert
        result.ShouldBeOfType<Result<WorkerRoleCatalogContent>.Failure>().Error.Message.ShouldContain($"roles/{path}");
    }

    [Theory]
    [InlineData("implement/prompts/system.md", "Branch naming: {branchNamingInstruction}. {issueNumber}")]
    [InlineData("implement/prompts/task.md", "Implement #{issueNumber} titled {issueTitle}.")]
    public void WhenARolePromptUsesAPlaceholderFoundryDoesNotFill_FailsNamingIt(string path, string template)
    {
        // Arrange
        Dictionary<string, string> files = ValidFiles();
        files[path] = template;

        // Act
        Result<WorkerRoleCatalogContent> result = WorkerRoleCatalogReader.Parse(files);

        // Assert
        Error error = result.ShouldBeOfType<Result<WorkerRoleCatalogContent>.Failure>().Error;
        error.Message.ShouldContain("placeholders Foundry does not fill");
    }

    [Fact]
    public void WhenTheTaskPromptLacksTheIssueNumber_Fails()
    {
        // Arrange
        Dictionary<string, string> files = ValidFiles();
        files["implement/prompts/task.md"] = "Implement the issue.";

        // Act
        Result<WorkerRoleCatalogContent> result = WorkerRoleCatalogReader.Parse(files);

        // Assert
        result.ShouldBeOfType<Result<WorkerRoleCatalogContent>.Failure>().Error.Message
            .ShouldContain(RolePromptPlaceholders.IssueNumber);
    }

    [Fact]
    public void WhenAPluginManifestIsNotValidJson_Fails()
    {
        // Arrange
        Dictionary<string, string> files = ValidFiles();
        files["implement/.claude-plugin/plugin.json"] = "{ not json";

        // Act
        Result<WorkerRoleCatalogContent> result = WorkerRoleCatalogReader.Parse(files);

        // Assert
        result.ShouldBeOfType<Result<WorkerRoleCatalogContent>.Failure>().Error.Message.ShouldContain("not valid JSON");
    }

    [Fact]
    public void WhenThePluginHasSkillsAndAgents_ReadsThemByFrontmatterNameAndDescription()
    {
        // Arrange
        Dictionary<string, string> files = ValidFiles();
        files["implement/skills/tdd/SKILL.md"] =
            "---\nname: test-first\ndescription: \"Write the failing test first.\"\n---\n\nBody of the skill.\n";
        files["implement/skills/tdd/reference.md"] = "Supporting file, not a skill.";
        files["implement/agents/reviewer.md"] = "---\ndescription: Reviews the diff.\n---\nAgent body.";
        files["shared/skills/git/SKILL.md"] = "No frontmatter here.";

        // Act
        WorkerRoleCatalogContent catalog = WorkerRoleCatalogReader.Parse(files)
            .ShouldBeOfType<Result<WorkerRoleCatalogContent>.Success>().Value;

        // Assert
        RolePlugin plugin = catalog.Find(WorkerRole.Implement).ShouldNotBeNull().Plugin;
        RolePluginComponent skill = plugin.Skills.ShouldHaveSingleItem();
        RolePluginComponent agent = plugin.Agents.ShouldHaveSingleItem();
        RolePluginComponent sharedSkill = catalog.Shared.Skills.ShouldHaveSingleItem();
        plugin.ShouldSatisfyAllConditions(
            () => skill.Name.ShouldBe("test-first"),
            () => skill.Description.ShouldBe("Write the failing test first."),
            () => skill.Path.ShouldBe("roles/implement/skills/tdd/SKILL.md"),
            () => skill.Content.ShouldContain("Body of the skill."),
            () => agent.Name.ShouldBe("reviewer"),
            () => agent.Description.ShouldBe("Reviews the diff."),
            () => sharedSkill.Name.ShouldBe("git"),
            () => sharedSkill.Description.ShouldBeNull());
    }

    [Fact]
    public void WhenARoleHasAVerdictSchema_ReadsIt()
    {
        // Arrange
        Dictionary<string, string> files = ValidFiles();
        files["implement/verdict.schema.json"] = """{"type":"object"}""";

        // Act
        WorkerRoleCatalogContent catalog = WorkerRoleCatalogReader.Parse(files)
            .ShouldBeOfType<Result<WorkerRoleCatalogContent>.Success>().Value;

        // Assert
        catalog.Find(WorkerRole.Implement).ShouldNotBeNull().VerdictSchema.ShouldBe("""{"type":"object"}""");
    }
}
