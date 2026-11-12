using System.Globalization;
using System.Text;

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
        ["triage/.claude-plugin/plugin.json"] = """{"name":"triage","description":"Triages an issue."}""",
        ["triage/prompts/system.md"] = "You are triaging issue #{issueNumber}.\n\n{issueContent}\n",
        ["triage/prompts/task.md"] = "Triage issue #{issueNumber}.\n",
        ["shared/.claude-plugin/plugin.json"] = """{"name":"shared","description":"Loaded for every role."}""",
        ["shared/prompts/principles.md"] = "\uFEFFEngineering principles.\r\n",
        ["shared/prompts/principles/testing.md"] = "Testing:\r\n- Tests first.\r\n",
        ["shared/prompts/principles/design.md"] = "Design:\n- Safe defaults.\n",
        ["shared/prompts/principles/code.md"] = "Code:\n- Comments explain why.\n",
        ["shared/prompts/principles/domain.md"] = "Domain modelling:\n- Domain terms.\n",
    };

    private static string FillToBytes(string unit, int bytes) =>
        string.Concat(Enumerable.Repeat(unit, bytes / Encoding.UTF8.GetByteCount(unit)));

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
            () => catalog.Shared.Description.ShouldBe("Loaded for every role."),
            () => catalog.Principles.Framing.ShouldBe("Engineering principles."),
            () => catalog.Principles.TextOf(PrincipleSection.Testing).ShouldBe("Testing:\n- Tests first."),
            () => catalog.Principles.TextOf(PrincipleSection.Design).ShouldBe("Design:\n- Safe defaults."),
            () => catalog.Principles.TextOf(PrincipleSection.Code).ShouldBe("Code:\n- Comments explain why."),
            () => catalog.Principles.TextOf(PrincipleSection.DomainModelling).ShouldBe("Domain modelling:\n- Domain terms."));
    }

    [Fact]
    public void WhenTheCatalogLoads_EachRoleCarriesTheCapabilityDeclaredInCode()
    {
        // Act
        WorkerRoleCatalogContent catalog = WorkerRoleCatalogReader.Parse(ValidFiles())
            .ShouldBeOfType<Result<WorkerRoleCatalogContent>.Success>().Value;

        // Assert
        catalog.ShouldSatisfyAllConditions(
            () => catalog.Find(WorkerRole.Triage).ShouldNotBeNull().Capability.ShouldBe(WorkerCapability.Restricted),
            () => catalog.Find(WorkerRole.Implement).ShouldNotBeNull().Capability.ShouldBe(WorkerCapability.Full));
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

    public static TheoryData<string> EveryPrinciplesFile() =>
    [
        "shared/prompts/principles.md",
        "shared/prompts/principles/testing.md",
        "shared/prompts/principles/design.md",
        "shared/prompts/principles/code.md",
        "shared/prompts/principles/domain.md",
    ];

    [Theory]
    [MemberData(nameof(EveryPrinciplesFile))]
    public void WhenAPrinciplesFileIsMissing_FailsNamingTheFile(string path)
    {
        // Arrange
        Dictionary<string, string> files = ValidFiles();
        files.Remove(path);

        // Act
        Result<WorkerRoleCatalogContent> result = WorkerRoleCatalogReader.Parse(files);

        // Assert
        Error error = result.ShouldBeOfType<Result<WorkerRoleCatalogContent>.Failure>().Error;
        error.ShouldSatisfyAllConditions(
            () => error.Code.ShouldBe(WorkerRoleCatalogReader.InvalidCatalogCode),
            () => error.Message.ShouldContain($"roles/{path}"));
    }

    [Theory]
    [MemberData(nameof(EveryPrinciplesFile))]
    public void WhenAPrinciplesFileIsBlank_FailsNamingTheFile(string path)
    {
        // Arrange
        Dictionary<string, string> files = ValidFiles();
        files[path] = " \r\n ";

        // Act
        Result<WorkerRoleCatalogContent> result = WorkerRoleCatalogReader.Parse(files);

        // Assert
        Error error = result.ShouldBeOfType<Result<WorkerRoleCatalogContent>.Failure>().Error;
        error.ShouldSatisfyAllConditions(
            () => error.Code.ShouldBe(WorkerRoleCatalogReader.InvalidCatalogCode),
            () => error.Message.ShouldContain($"roles/{path}"));
    }

    [Theory]
    [MemberData(nameof(EveryPrinciplesFile))]
    public void WhenAPrinciplesFileContainsAPlaceholder_FailsNamingTheFile(string path)
    {
        // Arrange
        Dictionary<string, string> files = ValidFiles();
        files[path] = "Issue #{issueNumber}.";

        // Act
        Result<WorkerRoleCatalogContent> result = WorkerRoleCatalogReader.Parse(files);

        // Assert
        Error error = result.ShouldBeOfType<Result<WorkerRoleCatalogContent>.Failure>().Error;
        error.ShouldSatisfyAllConditions(
            () => error.Message.ShouldContain("placeholders Foundry does not fill"),
            () => error.Message.ShouldContain($"roles/{path}"));
    }

    [Theory]
    [InlineData("a")]
    [InlineData("\u00e9")]
    public void WhenTheFramingAndEverySectionAreExactlyAtTheByteCap_Passes(string unit)
    {
        // Arrange
        Dictionary<string, string> files = ValidFiles();
        files["shared/prompts/principles.md"] = FillExactly(unit, FramingRoomUnderTheCap(files));

        // Act
        Result<WorkerRoleCatalogContent> result = WorkerRoleCatalogReader.Parse(files);

        // Assert
        WorkerRoleCatalogContent catalog = result.ShouldBeOfType<Result<WorkerRoleCatalogContent>.Success>().Value;
        Encoding.UTF8.GetByteCount(catalog.Principles.AssembleAll()).ShouldBe(WorkerRoleCatalogReader.MaxPrinciplesBytes);
    }

    [Theory]
    [InlineData("a")]
    [InlineData("\u00e9")]
    public void WhenTheFramingAndEverySectionExceedTheByteCapByOneByte_FailsNamingTheFilesAndTheCap(string unit)
    {
        // Arrange
        Dictionary<string, string> files = ValidFiles();
        files["shared/prompts/principles.md"] = FillExactly(unit, FramingRoomUnderTheCap(files)) + "a";

        // Act
        Result<WorkerRoleCatalogContent> result = WorkerRoleCatalogReader.Parse(files);

        // Assert
        Error error = result.ShouldBeOfType<Result<WorkerRoleCatalogContent>.Failure>().Error;
        error.ShouldSatisfyAllConditions(
            () => error.Code.ShouldBe(WorkerRoleCatalogReader.InvalidCatalogCode),
            () => error.Message.ShouldContain("roles/shared/prompts/principles.md"),
            () => error.Message.ShouldContain("roles/shared/prompts/principles/"),
            () => error.Message.ShouldContain(WorkerRoleCatalogReader.MaxPrinciplesBytes.ToString(CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void WhenOnlyTheSectionsTogetherExceedTheByteCap_Fails()
    {
        // Arrange: no single file is over the cap, but every role's largest possible text is.
        Dictionary<string, string> files = ValidFiles();
        string quarter = FillToBytes("a", WorkerRoleCatalogReader.MaxPrinciplesBytes / 4);
        files["shared/prompts/principles/testing.md"] = quarter;
        files["shared/prompts/principles/design.md"] = quarter;
        files["shared/prompts/principles/code.md"] = quarter;
        files["shared/prompts/principles/domain.md"] = quarter;

        // Act
        Result<WorkerRoleCatalogContent> result = WorkerRoleCatalogReader.Parse(files);

        // Assert
        result.ShouldBeOfType<Result<WorkerRoleCatalogContent>.Failure>().Error
            .Message.ShouldContain(WorkerRoleCatalogReader.MaxPrinciplesBytes.ToString(CultureInfo.InvariantCulture));
    }

    // Fills with the multi-byte unit, then pads with single bytes, so any byte count is reached exactly.
    private static string FillExactly(string unit, int bytes)
    {
        string filled = FillToBytes(unit, bytes);
        return filled + new string('a', bytes - Encoding.UTF8.GetByteCount(filled));
    }

    // The bytes the framing line may take so the framing with every section, as assembled, is exactly the cap.
    private static int FramingRoomUnderTheCap(Dictionary<string, string> files)
    {
        files["shared/prompts/principles.md"] = "x";
        WorkerPrinciples principles = WorkerRoleCatalogReader.Parse(files)
            .ShouldBeOfType<Result<WorkerRoleCatalogContent>.Success>().Value.Principles;

        return WorkerRoleCatalogReader.MaxPrinciplesBytes - (Encoding.UTF8.GetByteCount(principles.AssembleAll()) - 1);
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

    [Theory]
    [InlineData("""{"type":""")]
    [InlineData("""["not","an","object"]""")]
    public void WhenARoleVerdictSchemaIsNotAJsonObject_FailsNamingTheFile(string schema)
    {
        // Arrange
        Dictionary<string, string> files = ValidFiles();
        files["implement/verdict.schema.json"] = schema;

        // Act
        Result<WorkerRoleCatalogContent> result = WorkerRoleCatalogReader.Parse(files);

        // Assert
        Error error = result.ShouldBeOfType<Result<WorkerRoleCatalogContent>.Failure>().Error;
        error.ShouldSatisfyAllConditions(
            () => error.Code.ShouldBe(WorkerRoleCatalogReader.InvalidCatalogCode),
            () => error.Message.ShouldContain("roles/implement/verdict.schema.json"));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""[]""")]
    public void WhenTheSharedVerdictDefinitionsAreNotAJsonObject_FailsNamingTheFile(string definitions)
    {
        // Arrange
        Dictionary<string, string> files = ValidFiles();
        files["shared/verdict.defs.json"] = definitions;

        // Act
        Result<WorkerRoleCatalogContent> result = WorkerRoleCatalogReader.Parse(files);

        // Assert
        result.ShouldBeOfType<Result<WorkerRoleCatalogContent>.Failure>().Error.Message
            .ShouldContain("roles/shared/verdict.defs.json");
    }
}
