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
        Dictionary<string, string[]> requiredByOutcome = schema["allOf"]!.AsArray().ToDictionary(
            condition => condition!["if"]!["properties"]![ImplementVerdict.OutcomeProperty]!["const"]!.GetValue<string>(),
            condition => condition!["then"]!["required"]!.AsArray().Select(node => node!.GetValue<string>()).ToArray());
        string[] properties = [.. schema["properties"]!.AsObject().Select(pair => pair.Key)];

        // Assert
        schema.ShouldSatisfyAllConditions(
            () => outcomes.ShouldBe([ImplementVerdict.DoneToken, ImplementVerdict.NeedsInputToken]),
            () => required.ShouldBe([ImplementVerdict.OutcomeProperty]),
            () => properties.ShouldBe(
                [ImplementVerdict.OutcomeProperty, ImplementVerdict.QuestionsProperty, ImplementVerdict.PullRequestProperty],
                ignoreOrder: true),
            () => requiredByOutcome.Count.ShouldBe(2),
            () => requiredByOutcome[ImplementVerdict.NeedsInputToken].ShouldBe([ImplementVerdict.QuestionsProperty]),
            () => requiredByOutcome[ImplementVerdict.DoneToken].ShouldBe([ImplementVerdict.PullRequestProperty]),
            () => schema["properties"]![ImplementVerdict.QuestionsProperty]!["$ref"]!.GetValue<string>().ShouldBe("#/$defs/questions"),
            () => schema["additionalProperties"]!.GetValue<bool>().ShouldBeFalse());
    }

    [Fact]
    public void TheImplementVerdictPullRequestSchema_MatchesTheTypedPullRequest()
    {
        // Arrange
        JsonObject schema = ReadJsonObject(Path.Combine(RolesDirectory, WorkerRole.Implement.Name, WorkerRoleCatalogReader.VerdictSchemaPath));

        // Act
        JsonNode pullRequest = schema["properties"]![ImplementVerdict.PullRequestProperty]!;
        JsonNode summary = pullRequest["properties"]![ImplementVerdict.SummaryProperty]!;
        JsonNode body = pullRequest["properties"]![ImplementVerdict.BodyProperty]!;
        string[] required = [.. pullRequest["required"]!.AsArray().Select(node => node!.GetValue<string>())];
        string[] properties = [.. pullRequest["properties"]!.AsObject().Select(pair => pair.Key)];

        // Assert
        pullRequest.ShouldSatisfyAllConditions(
            () => pullRequest["type"]!.GetValue<string>().ShouldBe("object"),
            () => pullRequest["additionalProperties"]!.GetValue<bool>().ShouldBeFalse(),
            () => required.ShouldBe([ImplementVerdict.SummaryProperty, ImplementVerdict.BodyProperty], ignoreOrder: true),
            () => properties.ShouldBe([ImplementVerdict.SummaryProperty, ImplementVerdict.BodyProperty], ignoreOrder: true),
            () => summary["type"]!.GetValue<string>().ShouldBe("string"),
            () => summary["minLength"]!.GetValue<int>().ShouldBe(1),
            () => summary["maxLength"]!.GetValue<int>().ShouldBe(PullRequestSummary.MaxLength),
            () => summary["pattern"]!.GetValue<string>().ShouldBe(@"^[^\r\n]+$"),
            () => body["type"]!.GetValue<string>().ShouldBe("string"),
            () => body["minLength"]!.GetValue<int>().ShouldBe(1),
            () => body["maxLength"]!.GetValue<int>().ShouldBe(PullRequestDescription.MaxLength));
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
    [InlineData("""{"outcome":"done","pullRequest":{"summary":"add retry to webhook delivery","body":"Adds retry."}}""", RunVerdictStatus.Done)]
    [InlineData("""{"outcome":"done"}""", RunVerdictStatus.Invalid)]
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

        // Act
        Result<string> result = SystemPromptBuilder.Build(
            catalog.Principles,
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
            () => prompt.Split(catalog.Principles).Length.ShouldBe(2),
            () => prompt.IndexOf(catalog.Principles, StringComparison.Ordinal)
                .ShouldBeLessThan(prompt.IndexOf(applyLine, StringComparison.Ordinal)),
            () => prompt.ShouldNotContain("Test-driven development:"),
            () => prompt.ShouldNotContain(RolePromptPlaceholders.IssueNumber),
            () => prompt.ShouldNotContain(RolePromptPlaceholders.IssueContent),
            () => prompt.ShouldContain("Foundry read the issue's title, body and comments"),
            () => prompt.ShouldContain("<issue-title>Add retry</issue-title>"),
            () => prompt.ShouldNotContain("read the full issue from the provider"));
    }

    [Fact]
    public void TheImplementTaskPrompt_AsksForTheImplementationCommittedOnTheProvidedBranchNotAPushOrPullRequest()
    {
        // Arrange
        WorkerRoleDefinition implement = LoadRepositoryCatalog().Find(WorkerRole.Implement).ShouldNotBeNull();

        // Act
        string task = implement.TaskPromptTemplate;

        // Assert
        task.ShouldSatisfyAllConditions(
            () => task.ShouldContain("commit", Case.Insensitive),
            () => task.ShouldContain("branch Foundry provides"),
            () => task.ShouldNotContain("push", Case.Insensitive),
            () => task.ShouldNotContain("pull request", Case.Insensitive));
    }

    [Theory]
    [MemberData(nameof(RoleNames))]
    public void NoRolePrompt_AsksTheAgentToPushOrOpenAPullRequest(string roleName)
    {
        // Arrange
        string[] prompts =
        [
            File.ReadAllText(Path.Combine(RolesDirectory, roleName, WorkerRoleCatalogReader.SystemPromptPath)),
            File.ReadAllText(Path.Combine(RolesDirectory, roleName, WorkerRoleCatalogReader.TaskPromptPath)),
        ];

        // Assert
        prompts.ShouldSatisfyAllConditions(
            () => prompts.ShouldAllBe(prompt => !prompt.Contains("push", StringComparison.OrdinalIgnoreCase)),
            () => prompts.ShouldAllBe(prompt => !prompt.Contains("open a pull request", StringComparison.OrdinalIgnoreCase)));
    }

    [Theory]
    [InlineData("Testing (where a test runner exists):")]
    [InlineData("Design:")]
    [InlineData("Domain modelling (where the code models a business domain):")]
    public void ThePrinciplesFile_CarriesTheScopingHeadings(string heading)
    {
        // Arrange
        WorkerRoleCatalogContent catalog = LoadRepositoryCatalog();

        // Act
        string principles = catalog.Principles;

        // Assert
        principles.ShouldContain(heading);
    }

    private static WorkerRoleCatalogContent LoadRepositoryCatalog() =>
        WorkerRoleCatalogReader.ReadFromDirectory(RepositoryRoot.WorkersDirectory)
            .ShouldBeOfType<Result<WorkerRoleCatalogContent>.Success>().Value;
}
