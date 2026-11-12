using System.Text.Json.Nodes;

using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.ContainerSpec;
using Foundry.Modules.Workers.Features.Roles;
using Foundry.Modules.Workers.Features.Verdicts;
using Foundry.Shared;
using Foundry.UnitTests.Fakes.Workers;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Roles.RepositoryRolesTests;

/// <summary>
/// Guards the role folders checked into <c>workers/roles/</c> against the roles and placeholders defined in code
/// (ADR 0095): a role without a folder, or a prompt using a placeholder Foundry does not fill, fails here rather than
/// at image build or dispatch.
/// </summary>
public sealed class RepositoryRoles
{
    private static readonly string RolesDirectory =
        Path.Combine(RepositoryRoot.WorkersDirectory, WorkerRoleCatalogReader.RolesFolder);

    public static TheoryData<string> RoleNames => [.. WorkerRole.All.Select(role => role.Name)];

    [Theory]
    [MemberData(nameof(RoleNames))]
    public void EveryRoleDefinedInCode_HasAPluginManifestAndPromptsInTheRepository(string roleName)
    {
        // Arrange
        string roleDirectory = Path.Combine(RolesDirectory, roleName);

        // Assert
        roleDirectory.ShouldSatisfyAllConditions(
            () => File.Exists(Path.Combine(roleDirectory, WorkerRoleCatalogReader.PluginManifestPath)).ShouldBeTrue(),
            () => File.Exists(Path.Combine(roleDirectory, WorkerRoleCatalogReader.SystemPromptPath)).ShouldBeTrue(),
            () => File.Exists(Path.Combine(roleDirectory, WorkerRoleCatalogReader.TaskPromptPath)).ShouldBeTrue());
    }

    [Fact]
    public void TheSharedPluginLoadedForEveryRole_ExistsInTheRepository()
    {
        // Assert
        File.Exists(Path.Combine(RolesDirectory, WorkerRole.SharedFolder, WorkerRoleCatalogReader.PluginManifestPath))
            .ShouldBeTrue();
    }

    [Theory]
    [MemberData(nameof(RoleNames))]
    public void RoleSystemPrompt_UsesOnlyPlaceholdersFoundryFills(string roleName)
    {
        // Arrange
        string template = File.ReadAllText(Path.Combine(RolesDirectory, roleName, WorkerRoleCatalogReader.SystemPromptPath));

        // Act
        IReadOnlyList<string> placeholders = RolePromptPlaceholders.FindIn(template);

        // Assert
        placeholders.ShouldAllBe(placeholder => RolePromptPlaceholders.SystemPrompt.Contains(placeholder));
    }

    [Theory]
    [MemberData(nameof(RoleNames))]
    public void RoleTaskPrompt_UsesOnlyPlaceholdersFoundryFillsAndRequiresTheIssueNumber(string roleName)
    {
        // Arrange
        string template = File.ReadAllText(Path.Combine(RolesDirectory, roleName, WorkerRoleCatalogReader.TaskPromptPath));

        // Act
        IReadOnlyList<string> placeholders = RolePromptPlaceholders.FindIn(template);

        // Assert
        placeholders.ShouldSatisfyAllConditions(
            () => placeholders.ShouldAllBe(placeholder => RolePromptPlaceholders.TaskPrompt.Contains(placeholder)),
            () => placeholders.ShouldContain(RolePromptPlaceholders.IssueNumber));
    }

    [Fact]
    public void TheRepositoryRoleCatalog_Loads()
    {
        // Act
        Result<WorkerRoleCatalogContent> result = WorkerRoleCatalogReader.ReadFromDirectory(RepositoryRoot.WorkersDirectory);

        // Assert
        WorkerRoleCatalogContent catalog = result.ShouldBeOfType<Result<WorkerRoleCatalogContent>.Success>().Value;
        catalog.Roles.Select(role => role.Role).ShouldBe(WorkerRole.All);
    }

    [Fact]
    public void TheImplementRole_HasAVerdictSchema()
    {
        // Act
        WorkerRoleCatalogContent catalog = LoadRepositoryCatalog();

        // Assert
        catalog.Find(WorkerRole.Implement).ShouldNotBeNull().VerdictSchema.ShouldNotBeNull();
    }

    [Fact]
    public void TheImplementVerdictSchema_MatchesTheTypedImplementVerdict()
    {
        // Arrange
        JsonObject schema = ReadJsonObject(Path.Combine(RolesDirectory, WorkerRole.Implement.Name, WorkerRoleCatalogReader.VerdictSchemaPath));

        // Act
        string[] outcomes = [.. schema["properties"]![ImplementVerdict.OutcomeProperty]!["enum"]!.AsArray().Select(node => node!.GetValue<string>())];
        string[] required = [.. schema["required"]!.AsArray().Select(node => node!.GetValue<string>())];
        string? conditionalOutcome = schema["if"]?["properties"]?[ImplementVerdict.OutcomeProperty]?["const"]?.GetValue<string>();
        string[] conditionallyRequired = [.. schema["then"]!["required"]!.AsArray().Select(node => node!.GetValue<string>())];

        // Assert
        schema.ShouldSatisfyAllConditions(
            () => outcomes.ShouldBe([ImplementVerdict.DoneToken, ImplementVerdict.NeedsInputToken]),
            () => required.ShouldBe([ImplementVerdict.OutcomeProperty]),
            () => conditionalOutcome.ShouldBe(ImplementVerdict.NeedsInputToken),
            () => conditionallyRequired.ShouldBe([ImplementVerdict.QuestionsProperty]),
            () => schema["properties"]![ImplementVerdict.QuestionsProperty]!["$ref"]!.GetValue<string>().ShouldBe("#/$defs/questions"),
            () => schema["additionalProperties"]!.GetValue<bool>().ShouldBeFalse());
    }

    [Fact]
    public void TheSharedQuestionsDefinition_MatchesTheQuestionsReader()
    {
        // Arrange
        JsonObject definitions = ReadJsonObject(
            Path.Combine(RolesDirectory, WorkerRole.SharedFolder, WorkerRoleCatalogReader.SharedVerdictDefinitionsPath));

        // Act
        JsonNode questions = definitions["$defs"]!["questions"]!;
        JsonNode items = questions["items"]!;
        string[] itemRequired = [.. items["required"]!.AsArray().Select(node => node!.GetValue<string>())];

        // Assert
        questions.ShouldSatisfyAllConditions(
            () => questions["type"]!.GetValue<string>().ShouldBe("array"),
            () => questions["minItems"]!.GetValue<int>().ShouldBe(1),
            () => items["additionalProperties"]!.GetValue<bool>().ShouldBeFalse(),
            () => itemRequired.ShouldBe([VerdictQuestionsReader.QuestionProperty, VerdictQuestionsReader.ContextProperty]),
            () => items["properties"]![VerdictQuestionsReader.QuestionProperty]!["minLength"]!.GetValue<int>().ShouldBe(1));
    }

    [Theory]
    [MemberData(nameof(RoleNames))]
    public void EveryRoleVerdictSchemaReference_ResolvesToASharedOrOwnDefinition(string roleName)
    {
        // Arrange
        string schemaPath = Path.Combine(RolesDirectory, roleName, WorkerRoleCatalogReader.VerdictSchemaPath);

        if (!File.Exists(schemaPath))
        {
            return;
        }

        JsonObject schema = ReadJsonObject(schemaPath);
        JsonObject shared = ReadJsonObject(
            Path.Combine(RolesDirectory, WorkerRole.SharedFolder, WorkerRoleCatalogReader.SharedVerdictDefinitionsPath));
        HashSet<string> definitions = [
            .. (schema["$defs"] as JsonObject ?? []).Select(pair => pair.Key),
            .. (shared["$defs"] as JsonObject ?? []).Select(pair => pair.Key)];

        // Act
        List<string> references = [];
        CollectReferences(schema, references);
        const string DefsPrefix = "#/$defs/";
        string[] unresolved = [.. references.Where(reference =>
            !reference.StartsWith(DefsPrefix, StringComparison.Ordinal)
            || !definitions.Contains(reference[DefsPrefix.Length..]))];

        // Assert
        unresolved.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("""{"outcome":"done"}""", RunVerdictStatus.Done)]
    [InlineData("""{"outcome":"needs_input","questions":[{"question":"Which database?","context":"The issue names two."}]}""", RunVerdictStatus.NeedsInput)]
    public void ASampleMatchingTheImplementVerdictSchema_ParsesWithTheImplementVerdictParser(string sample, RunVerdictStatus expected)
    {
        // Act
        RunVerdict verdict = new ImplementVerdictParser().Parse(JsonNode.Parse(sample)!.AsObject());

        // Assert
        verdict.Status.ShouldBe(expected);
    }

    [Fact]
    public void TheTriageRole_HasAVerdictSchema()
    {
        // Act
        WorkerRoleCatalogContent catalog = LoadRepositoryCatalog();

        // Assert
        catalog.Find(WorkerRole.Triage).ShouldNotBeNull().VerdictSchema.ShouldNotBeNull();
    }

    [Fact]
    public void TheTriageVerdictSchema_MatchesTheTypedTriageVerdict()
    {
        // Arrange
        JsonObject schema = ReadJsonObject(Path.Combine(RolesDirectory, WorkerRole.Triage.Name, WorkerRoleCatalogReader.VerdictSchemaPath));

        // Act
        string[] outcomes = [.. schema["properties"]![TriageVerdict.OutcomeProperty]!["enum"]!.AsArray().Select(node => node!.GetValue<string>())];
        string[] kinds = [.. schema["properties"]![TriageVerdict.KindProperty]!["enum"]!.AsArray().Select(node => node!.GetValue<string>())];
        string[] required = [.. schema["required"]!.AsArray().Select(node => node!.GetValue<string>())];
        string? conditionalOutcome = schema["if"]?["properties"]?[TriageVerdict.OutcomeProperty]?["const"]?.GetValue<string>();
        string[] conditionallyRequired = [.. schema["then"]!["required"]!.AsArray().Select(node => node!.GetValue<string>())];

        // Assert
        schema.ShouldSatisfyAllConditions(
            () => outcomes.ShouldBe([TriageVerdict.ReadyToken, TriageVerdict.NeedsInputToken]),
            () => kinds.ShouldBe(TriageVerdict.KindTokens.Keys, ignoreOrder: true),
            () => required.ShouldBe([TriageVerdict.OutcomeProperty, TriageVerdict.KindProperty], ignoreOrder: true),
            () => conditionalOutcome.ShouldBe(TriageVerdict.NeedsInputToken),
            () => conditionallyRequired.ShouldBe([TriageVerdict.QuestionsProperty]),
            () => schema["properties"]![TriageVerdict.QuestionsProperty]!["$ref"]!.GetValue<string>().ShouldBe("#/$defs/questions"),
            () => schema["properties"]!.AsObject().Select(pair => pair.Key).ShouldBe(
                [TriageVerdict.OutcomeProperty, TriageVerdict.KindProperty, TriageVerdict.QuestionsProperty],
                ignoreOrder: true),
            () => schema["additionalProperties"]!.GetValue<bool>().ShouldBeFalse());
    }

    [Theory]
    [InlineData("""{"outcome":"ready","kind":"documentation"}""", RunVerdictStatus.Done)]
    [InlineData("""{"outcome":"needs_input","kind":"bug","questions":[{"question":"Which page?","context":"None is named."}]}""", RunVerdictStatus.NeedsInput)]
    public void ASampleMatchingTheTriageVerdictSchema_ParsesWithTheTriageVerdictParser(string sample, RunVerdictStatus expected)
    {
        // Act
        RunVerdict verdict = new TriageVerdictParser().Parse(JsonNode.Parse(sample)!.AsObject());

        // Assert
        verdict.Status.ShouldBe(expected);
    }

    private static JsonObject ReadJsonObject(string path) =>
        JsonNode.Parse(File.ReadAllText(path))!.AsObject();

    private static void CollectReferences(JsonNode? node, List<string> references)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (KeyValuePair<string, JsonNode?> property in obj)
                {
                    if (property.Key == "$ref" && property.Value is JsonValue value)
                    {
                        references.Add(value.GetValue<string>());
                    }
                    else
                    {
                        CollectReferences(property.Value, references);
                    }
                }

                break;
            case JsonArray array:
                foreach (JsonNode? item in array)
                {
                    CollectReferences(item, references);
                }

                break;
        }
    }

    [Fact]
    public void WhenTheImplementRolePromptIsRendered_PlacesTheRepositoryPrinciplesBeforeTheApplyLine()
    {
        // Arrange
        const string applyLine = "Apply the engineering principles above to every change.";
        WorkerRoleCatalogContent catalog = LoadRepositoryCatalog();
        WorkerRoleDefinition implement = catalog.Find(WorkerRole.Implement).ShouldNotBeNull();
        string principles = catalog.PrinciplesFor(WorkerRole.Implement);

        // Act
        Result<string> result = SystemPromptBuilder.Build(
            principles,
            42,
            implement.SystemPromptTemplate,
            new DispatchContext.Fresh("feat/42-title"),
            new IssueContent("Add retry", "Retry webhook delivery.", []),
            timeoutMinutes: 120,
            installsDocker: false,
            installsDotnet: false);

        // Assert
        string prompt = result.ShouldBeOfType<Result<string>.Success>().Value;
        prompt.ShouldSatisfyAllConditions(
            () => prompt.Split(principles).Length.ShouldBe(2),
            () => prompt.IndexOf(principles, StringComparison.Ordinal)
                .ShouldBeLessThan(prompt.IndexOf(applyLine, StringComparison.Ordinal)),
            () => prompt.ShouldNotContain("Test-driven development:"),
            () => prompt.ShouldNotContain(RolePromptPlaceholders.IssueNumber),
            () => prompt.ShouldNotContain(RolePromptPlaceholders.IssueContent),
            () => prompt.ShouldContain("Foundry read the issue's title, body and comments"),
            () => prompt.ShouldContain("<issue-title>Add retry</issue-title>"),
            () => prompt.ShouldNotContain("read the full issue from the provider"));
    }

    [Theory]
    [InlineData(PrincipleSection.Testing, "Testing (where a test runner exists):")]
    [InlineData(PrincipleSection.Design, "Design:")]
    [InlineData(PrincipleSection.Code, "Code:")]
    [InlineData(PrincipleSection.DomainModelling, "Domain modelling (where the code models a business domain):")]
    public void EachPrincipleSectionFile_OpensWithItsScopingHeading(PrincipleSection section, string heading)
    {
        // Arrange
        WorkerRoleCatalogContent catalog = LoadRepositoryCatalog();

        // Act
        string text = catalog.Principles.TextOf(section);

        // Assert
        text.ShouldStartWith(heading + "\n");
    }

    [Fact]
    public void TheFramingFile_HoldsOnlyTheFramingLine()
    {
        // Arrange
        WorkerRoleCatalogContent catalog = LoadRepositoryCatalog();

        // Act
        string framing = catalog.Principles.Framing;

        // Assert
        framing.ShouldBe(
            "Engineering principles. The repository's own style, testing and design conventions win where they differ; the safety rules above always apply.");
    }

    [Fact]
    public void WhenTheTriageRolePromptIsRendered_CarriesOnlyTheDesignAndDomainModellingPrinciples()
    {
        // Arrange
        const string judgeLine = "Judge the work against the engineering principles above.";
        WorkerRoleCatalogContent catalog = LoadRepositoryCatalog();
        WorkerRoleDefinition triage = catalog.Find(WorkerRole.Triage).ShouldNotBeNull();
        string principles = catalog.PrinciplesFor(WorkerRole.Triage);

        // Act
        Result<string> result = SystemPromptBuilder.BuildRestricted(
            principles,
            42,
            triage.SystemPromptTemplate,
            new IssueContent("Add retry", "Retry webhook delivery.", []),
            installsDotnet: false);

        // Assert
        string prompt = result.ShouldBeOfType<Result<string>.Success>().Value;
        prompt.ShouldSatisfyAllConditions(
            () => prompt.ShouldContain(catalog.Principles.Framing + "\n\n" + catalog.Principles.TextOf(PrincipleSection.Design)),
            () => prompt.ShouldContain(catalog.Principles.TextOf(PrincipleSection.DomainModelling)),
            () => prompt.ShouldNotContain(catalog.Principles.TextOf(PrincipleSection.Testing)),
            () => prompt.ShouldNotContain(catalog.Principles.TextOf(PrincipleSection.Code)),
            () => prompt.IndexOf(principles, StringComparison.Ordinal)
                .ShouldBeLessThan(prompt.IndexOf(judgeLine, StringComparison.Ordinal)));
    }

    private static WorkerRoleCatalogContent LoadRepositoryCatalog() =>
        WorkerRoleCatalogReader.ReadFromDirectory(RepositoryRoot.WorkersDirectory)
            .ShouldBeOfType<Result<WorkerRoleCatalogContent>.Success>().Value;
}
