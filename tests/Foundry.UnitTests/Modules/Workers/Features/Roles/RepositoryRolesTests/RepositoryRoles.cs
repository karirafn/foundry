using System.Text.Json;
using System.Text.Json.Nodes;

using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.ContainerSpec;
using Foundry.Modules.Workers.Features.Roles;
using Foundry.Modules.Workers.Features.Verdicts;
using Foundry.Shared;
using Foundry.Testing;
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

    private static readonly string[] ForbiddenTopLevelKeywords = ["allOf", "anyOf", "oneOf"];

    private const string SingleLinePattern = @"^[^\r\n]+$";

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
    public void NoRoleVerdictSchema_HasATopLevelAllOfAnyOfOrOneOf()
    {
        // Arrange
        WorkerRoleCatalogContent catalog = LoadRepositoryCatalog();

        // Act
        string[] offenders =
        [
            .. catalog.Roles.SelectMany(role => ForbiddenTopLevelKeywordsIn(role)),
        ];

        // Assert
        offenders.ShouldBeEmpty();
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
        string[] requiredWhenConditional = [.. schema["then"]!["required"]!.AsArray().Select(node => node!.GetValue<string>())];
        string[] requiredOtherwise = [.. schema["else"]!["required"]!.AsArray().Select(node => node!.GetValue<string>())];
        string[] properties = [.. schema["properties"]!.AsObject().Select(pair => pair.Key)];

        // Assert
        schema.ShouldSatisfyAllConditions(
            () => outcomes.ShouldBe([ImplementVerdict.DoneToken, ImplementVerdict.NeedsInputToken]),
            () => required.ShouldBe([ImplementVerdict.OutcomeProperty]),
            () => properties.ShouldBe(
                [ImplementVerdict.OutcomeProperty, ImplementVerdict.QuestionsProperty, ImplementVerdict.PullRequestProperty],
                ignoreOrder: true),
            () => conditionalOutcome.ShouldBe(ImplementVerdict.NeedsInputToken),
            () => requiredWhenConditional.ShouldBe([ImplementVerdict.QuestionsProperty]),
            () => requiredOtherwise.ShouldBe([ImplementVerdict.PullRequestProperty]),
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
        JsonNode Property(string name) => pullRequest["properties"]![name]!;
        string[] required = [.. pullRequest["required"]!.AsArray().Select(node => node!.GetValue<string>())];
        string[] properties = [.. pullRequest["properties"]!.AsObject().Select(pair => pair.Key)];

        // Assert
        pullRequest.ShouldSatisfyAllConditions(
            () => pullRequest["type"]!.GetValue<string>().ShouldBe("object"),
            () => pullRequest["additionalProperties"]!.GetValue<bool>().ShouldBeFalse(),
            () => required.ShouldBe(
                [ImplementVerdict.SummaryProperty, ImplementVerdict.ChangesProperty, ImplementVerdict.VerificationProperty],
                ignoreOrder: true),
            () => properties.ShouldBe(
                [
                    ImplementVerdict.SummaryProperty,
                    ImplementVerdict.ChangesProperty,
                    ImplementVerdict.VerificationProperty,
                    ImplementVerdict.CauseProperty,
                    ImplementVerdict.AttentionProperty,
                    ImplementVerdict.NotesProperty,
                ],
                ignoreOrder: true),
            () => AssertString(Property(ImplementVerdict.SummaryProperty), PullRequestSummary.MaxLength, singleLine: true),
            () => AssertLines(Property(ImplementVerdict.ChangesProperty), 1, PullRequestDescription.MaxChanges, PullRequestDescription.ItemMaxLength),
            () => AssertLines(
                Property(ImplementVerdict.VerificationProperty), 1, PullRequestDescription.MaxVerification, PullRequestDescription.ItemMaxLength),
            () => AssertLines(
                Property(ImplementVerdict.AttentionProperty), 0, PullRequestDescription.MaxAttention, PullRequestDescription.AttentionItemMaxLength),
            () => AssertString(Property(ImplementVerdict.CauseProperty), PullRequestDescription.CauseMaxLength, singleLine: false),
            () => AssertString(Property(ImplementVerdict.NotesProperty), PullRequestDescription.NotesMaxLength, singleLine: false));
    }

    private static void AssertString(JsonNode property, int maxLength, bool singleLine)
    {
        property["type"]!.GetValue<string>().ShouldBe("string");
        property["minLength"]!.GetValue<int>().ShouldBe(1);
        property["maxLength"]!.GetValue<int>().ShouldBe(maxLength);
        (property["pattern"]?.GetValue<string>()).ShouldBe(singleLine ? @"^[^\r\n]+$" : null);
    }

    private static void AssertLines(JsonNode property, int minItems, int maxItems, int itemMaxLength)
    {
        property["type"]!.GetValue<string>().ShouldBe("array");
        property["minItems"]!.GetValue<int>().ShouldBe(minItems);
        property["maxItems"]!.GetValue<int>().ShouldBe(maxItems);
        AssertString(property["items"]!, itemMaxLength, singleLine: true);
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
            () => itemRequired.ShouldBe([
                VerdictQuestionsReader.QuestionProperty,
                VerdictQuestionsReader.ProblemProperty,
                VerdictQuestionsReader.ImpactProperty,
                VerdictQuestionsReader.OptionsProperty,
                VerdictQuestionsReader.RecommendationProperty,
                VerdictQuestionsReader.EvidenceProperty]),
            () => items["properties"]![VerdictQuestionsReader.OptionsProperty]!["minItems"]!.GetValue<int>().ShouldBe(InputQuestion.Structured.MinOptions),
            () => items["properties"]![VerdictQuestionsReader.OptionsProperty]!["maxItems"]!.GetValue<int>().ShouldBe(InputQuestion.Structured.MaxOptions),
            () => items["properties"]![VerdictQuestionsReader.EvidenceProperty]!["maxItems"]!.GetValue<int>().ShouldBe(InputQuestion.Structured.MaxEvidence),
            () => items["properties"]![VerdictQuestionsReader.RecommendationProperty]!["required"]!.AsArray()
                .Select(node => node!.GetValue<string>())
                .ShouldBe([VerdictQuestionsReader.RecommendedOptionProperty, VerdictQuestionsReader.ReasonProperty]),
            () => items["properties"]![VerdictQuestionsReader.RecommendationProperty]!["properties"]![VerdictQuestionsReader.RecommendedOptionProperty]!["enum"]!.AsArray()
                .Select(node => node!.GetValue<string>())
                .ShouldBe(Enumerable.Range(0, InputQuestion.Structured.MaxOptions).Select(InputQuestion.Structured.LetterOf)));
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
    [InlineData("""{"outcome":"done","pullRequest":{"summary":"add retry to webhook delivery","changes":["Adds retry."],"verification":["dotnet test: passed."]}}""", RunVerdictStatus.Done)]
    [InlineData("""{"outcome":"done"}""", RunVerdictStatus.Invalid)]
    [InlineData("""{"outcome":"needs_input","questions":[{"question":"Which database?","problem":"Two are configured.","impact":"Migrations target the wrong one.","options":["SQLite","Postgres"],"recommendation":{"option":"b","reason":"Production runs it."},"evidence":["src/Db.cs:12 opens SQLite"]}]}""", RunVerdictStatus.NeedsInput)]
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
        JsonNode blockedBy = schema["properties"]![TriageVerdict.BlockedByProperty]!;
        JsonNode blockerProperties = blockedBy["items"]!["properties"]!;

        // Assert
        schema.ShouldSatisfyAllConditions(
            () => outcomes.ShouldBe([TriageVerdict.ReadyToken, TriageVerdict.NeedsInputToken, TriageVerdict.BlockedToken]),
            () => schema["else"]!["if"]!["properties"]![TriageVerdict.OutcomeProperty]!["const"]!.GetValue<string>().ShouldBe(TriageVerdict.BlockedToken),
            () => schema["else"]!["then"]!["not"]!["required"]!.AsArray().Select(node => node!.GetValue<string>()).ShouldBe([TriageVerdict.DecisionsProperty]),
            () => schema["then"]!.AsObject().Select(pair => pair.Key).ShouldBe(["required"]),
            () => blockedBy["type"]!.GetValue<string>().ShouldBe("array"),
            () => blockedBy["minItems"]!.GetValue<int>().ShouldBe(1),
            () => blockedBy["maxItems"]!.GetValue<int>().ShouldBe(TriageResult.Blocked.MaxBlockers),
            () => blockedBy["items"]!["additionalProperties"]!.GetValue<bool>().ShouldBeFalse(),
            () => blockedBy["items"]!["required"]!.AsArray().Select(node => node!.GetValue<string>()).ShouldBe(["issue", "reason"], ignoreOrder: true),
            () => blockerProperties["issue"]!["type"]!.GetValue<string>().ShouldBe("integer"),
            () => blockerProperties["reason"]!["minLength"]!.GetValue<int>().ShouldBe(1),
            () => blockerProperties["reason"]!["maxLength"]!.GetValue<int>().ShouldBe(TriageBlocker.ReasonMaxLength),
            () => kinds.ShouldBe(TriageVerdict.KindTokens.Keys, ignoreOrder: true),
            () => required.ShouldBe([TriageVerdict.OutcomeProperty, TriageVerdict.KindProperty], ignoreOrder: true),
            () => conditionalOutcome.ShouldBe(TriageVerdict.NeedsInputToken),
            () => conditionallyRequired.ShouldBe([TriageVerdict.QuestionsProperty]),
            () => schema["properties"]![TriageVerdict.QuestionsProperty]!["$ref"]!.GetValue<string>().ShouldBe("#/$defs/questions"),
            () => schema["properties"]!.AsObject().Select(pair => pair.Key).ShouldBe(
                [TriageVerdict.OutcomeProperty, TriageVerdict.KindProperty, TriageVerdict.QuestionsProperty, TriageVerdict.DecisionsProperty, TriageVerdict.BlockedByProperty],
                ignoreOrder: true),
            () => schema["additionalProperties"]!.GetValue<bool>().ShouldBeFalse());
    }

    [Fact]
    public void TheTriageVerdictSchema_DecisionsMatchTheTypedDecision()
    {
        // Arrange
        JsonObject schema = ReadJsonObject(Path.Combine(RolesDirectory, WorkerRole.Triage.Name, WorkerRoleCatalogReader.VerdictSchemaPath));

        // Act
        JsonNode decisions = schema["properties"]![TriageVerdict.DecisionsProperty]!;
        JsonNode item = decisions["items"]!;
        JsonNode properties = item["properties"]!;

        // Assert
        decisions.ShouldSatisfyAllConditions(
            () => decisions["type"]!.GetValue<string>().ShouldBe("array"),
            () => decisions["maxItems"]!.GetValue<int>().ShouldBe(TriageResult.MaxDecisions),
            () => item["additionalProperties"]!.GetValue<bool>().ShouldBeFalse(),
            () => item["required"]!.AsArray().Select(node => node!.GetValue<string>()).ShouldBe(
                [
                    VerdictDecisionsReader.QuestionProperty,
                    VerdictDecisionsReader.DecisionProperty,
                    VerdictDecisionsReader.GroundProperty,
                    VerdictDecisionsReader.EvidenceProperty,
                ],
                ignoreOrder: true),
            () => properties[VerdictDecisionsReader.QuestionProperty]!["maxLength"]!.GetValue<int>().ShouldBe(TriageDecision.QuestionMaxLength),
            () => properties[VerdictDecisionsReader.QuestionProperty]!["pattern"]!.GetValue<string>().ShouldBe("^[^\\r\\n]+$"),
            () => properties[VerdictDecisionsReader.DecisionProperty]!["maxLength"]!.GetValue<int>().ShouldBe(TriageDecision.DecisionMaxLength),
            () => properties[VerdictDecisionsReader.GroundProperty]!["enum"]!.AsArray().Select(node => node!.GetValue<string>())
                .ShouldBe(VerdictDecisionsReader.GroundTokens.Keys, ignoreOrder: true),
            () => properties[VerdictDecisionsReader.EvidenceProperty]!["minItems"]!.GetValue<int>().ShouldBe(1),
            () => properties[VerdictDecisionsReader.EvidenceProperty]!["maxItems"]!.GetValue<int>().ShouldBe(TriageDecision.MaxEvidence),
            () => properties[VerdictDecisionsReader.EvidenceProperty]!["items"]!["maxLength"]!.GetValue<int>().ShouldBe(TriageDecision.EvidenceMaxLength),
            () => properties[VerdictDecisionsReader.EvidenceProperty]!["items"]!["pattern"]!.GetValue<string>().ShouldBe("^[^\\r\\n]+$"));
    }

    [Theory]
    [InlineData("""{"outcome":"ready","kind":"documentation"}""", RunVerdictStatus.Done)]
    [InlineData("""{"outcome":"needs_input","kind":"bug","questions":[{"question":"Which page?","problem":"Two are configured.","impact":"Migrations target the wrong one.","options":["SQLite","Postgres"],"recommendation":{"option":"b","reason":"Production runs it."},"evidence":["src/Db.cs:12 opens SQLite"]}]}""", RunVerdictStatus.NeedsInput)]
    [InlineData("""{"outcome":"blocked","kind":"feature","blockedBy":[{"issue":15,"reason":"The retry policy lands there."}]}""", RunVerdictStatus.Done)]
    [InlineData("""{"outcome":"ready","kind":"feature","decisions":[{"question":"Which retry cap?","decision":"Three attempts.","ground":"settled_business","evidence":["src/Retry.cs:8 caps at three"]}]}""", RunVerdictStatus.Done)]
    public void ASampleMatchingTheTriageVerdictSchema_ParsesWithTheTriageVerdictParser(string sample, RunVerdictStatus expected)
    {
        // Act
        RunVerdict verdict = new TriageVerdictParser().Parse(JsonNode.Parse(sample)!.AsObject());

        // Assert
        verdict.Status.ShouldBe(expected);
    }

    private static readonly string[] ReviewCategories =
        ["correctness", "criteria", "tests", "security", "quality", "design", "conventions"];

    [Fact]
    public void TheReviewRole_HasAVerdictSchema()
    {
        // Act
        WorkerRoleCatalogContent catalog = LoadRepositoryCatalog();

        // Assert
        catalog.Find(WorkerRole.Review).ShouldNotBeNull().VerdictSchema.ShouldNotBeNull();
    }

    [Fact]
    public void TheReviewPluginManifest_NamesTheRoleAndStatesItsPurpose()
    {
        // Arrange
        JsonObject manifest = ReadJsonObject(
            Path.Combine(RolesDirectory, WorkerRole.Review.Name, WorkerRoleCatalogReader.PluginManifestPath));

        // Act
        RolePlugin plugin = LoadRepositoryCatalog().Find(WorkerRole.Review).ShouldNotBeNull().Plugin;

        // Assert
        manifest.ShouldSatisfyAllConditions(
            () => manifest["name"]!.GetValue<string>().ShouldBe("foundry-role-review"),
            () => plugin.Description.ShouldNotBeNullOrWhiteSpace());
    }

    [Fact]
    public void TheReviewVerdictSchema_RequiresExactlyTheFiveTopLevelPropertiesAndNoOutcome()
    {
        // Arrange
        JsonObject schema = ReadReviewSchema();

        // Act
        string[] properties = [.. schema["properties"]!.AsObject().Select(pair => pair.Key)];
        string[] required = [.. schema["required"]!.AsArray().Select(node => node!.GetValue<string>())];

        // Assert
        schema.ShouldSatisfyAllConditions(
            () => properties.ShouldBe(["summary", "findings", "dimensions", "criteria", "tests"], ignoreOrder: true),
            () => required.ShouldBe(properties, ignoreOrder: true),
            () => properties.ShouldNotContain("outcome"),
            () => schema["summary"].ShouldBeNull(),
            () => schema["properties"]!["summary"]!["minLength"]!.GetValue<int>().ShouldBe(1),
            () => schema["properties"]!["summary"]!["maxLength"]!.GetValue<int>().ShouldBe(300));
    }

    [Fact]
    public void TheReviewVerdictSchema_ForbidsAdditionalPropertiesOnEveryObject()
    {
        // Arrange
        JsonObject schema = ReadReviewSchema();
        List<string> open = [];

        // Act
        CollectOpenObjects(schema, "$", open);

        // Assert
        open.ShouldBeEmpty();
    }

    [Fact]
    public void TheReviewVerdictSchemaFindings_MatchTheIssueContract()
    {
        // Arrange
        JsonObject schema = ReadReviewSchema();

        // Act
        JsonNode findings = schema["properties"]!["findings"]!;
        JsonNode item = findings["items"]!;
        JsonNode properties = item["properties"]!;
        JsonNode rule = properties["rule"]!;

        // Assert
        findings.ShouldSatisfyAllConditions(
            () => findings["minItems"]!.GetValue<int>().ShouldBe(0),
            () => findings["maxItems"]!.GetValue<int>().ShouldBe(50),
            () => StringsOf(item["required"]).ShouldBe(["path", "severity", "category", "body"], ignoreOrder: true),
            () => properties.AsObject().Select(pair => pair.Key)
                .ShouldBe(["path", "line", "severity", "category", "rule", "body"], ignoreOrder: true),
            () => properties["path"]!["type"]!.GetValue<string>().ShouldBe("string"),
            () => properties["line"]!["type"]!.GetValue<string>().ShouldBe("integer"),
            () => properties["line"]!["minimum"]!.GetValue<int>().ShouldBe(1),
            () => StringsOf(properties["severity"]!["enum"]).ShouldBe(["blocking", "advisory"]),
            () => StringsOf(properties["category"]!["enum"]).ShouldBe(ReviewCategories),
            () => rule["required"].ShouldBeNull(),
            () => rule["properties"]!.AsObject().Select(pair => pair.Key).ShouldBe(["principle", "file", "quote"], ignoreOrder: true),
            () => properties["body"]!["minLength"]!.GetValue<int>().ShouldBe(1),
            () => properties["body"]!["maxLength"]!.GetValue<int>().ShouldBe(300),
            () => properties["body"]!["pattern"]!.GetValue<string>().ShouldBe(SingleLinePattern));
    }

    [Fact]
    public void TheReviewVerdictSchemaDimensions_RequireEveryCategoryWithAStatusAndANote()
    {
        // Arrange
        JsonObject schema = ReadReviewSchema();

        // Act
        JsonNode dimensions = schema["properties"]!["dimensions"]!;
        JsonNode dimension = schema["$defs"]!["dimension"]!;

        // Assert
        dimensions.ShouldSatisfyAllConditions(
            () => StringsOf(dimensions["required"]).ShouldBe(ReviewCategories, ignoreOrder: true),
            () => dimensions["properties"]!.AsObject().Select(pair => pair.Key).ShouldBe(ReviewCategories, ignoreOrder: true),
            () => dimensions["properties"]!.AsObject().ShouldAllBe(
                pair => pair.Value!["$ref"]!.GetValue<string>() == "#/$defs/dimension"),
            () => StringsOf(dimension["required"]).ShouldBe(["status", "note"], ignoreOrder: true),
            () => StringsOf(dimension["properties"]!["status"]!["enum"]).ShouldBe(["clear", "findings", "not_applicable"]),
            () => dimension["properties"]!["note"]!["maxLength"]!.GetValue<int>().ShouldBe(500));
    }

    [Fact]
    public void TheReviewVerdictSchemaCriteriaAndTests_MatchTheIssueContract()
    {
        // Arrange
        JsonObject schema = ReadReviewSchema();

        // Act
        JsonNode criteria = schema["properties"]!["criteria"]!;
        JsonNode criterion = criteria["items"]!["properties"]!;
        JsonNode tests = schema["properties"]!["tests"]!;
        JsonNode testProperties = tests["properties"]!;

        // Assert
        criteria.ShouldSatisfyAllConditions(
            () => criteria["minItems"]!.GetValue<int>().ShouldBe(0),
            () => criteria["maxItems"]!.GetValue<int>().ShouldBe(50),
            () => StringsOf(criteria["items"]!["required"]).ShouldBe(["id", "status", "evidence"], ignoreOrder: true),
            () => criterion["id"]!["pattern"]!.GetValue<string>().ShouldBe("^AC[0-9]+$"),
            () => StringsOf(criterion["status"]!["enum"]).ShouldBe(["met", "unmet", "not_verifiable", "covered_by_checks"]),
            () => criterion["evidence"]!["maxLength"]!.GetValue<int>().ShouldBe(200),
            () => criterion["evidence"]!["pattern"]!.GetValue<string>().ShouldBe(SingleLinePattern),
            () => StringsOf(tests["required"]).ShouldBe(["assessment", "reason", "coveringTests"], ignoreOrder: true),
            () => StringsOf(testProperties["assessment"]!["enum"]).ShouldBe(["adequate", "missing", "not_needed"]),
            () => testProperties["reason"]!["maxLength"]!.GetValue<int>().ShouldBe(200),
            () => testProperties["reason"]!["pattern"]!.GetValue<string>().ShouldBe(SingleLinePattern),
            () => testProperties["coveringTests"]!["minItems"]!.GetValue<int>().ShouldBe(0),
            () => testProperties["coveringTests"]!["maxItems"]!.GetValue<int>().ShouldBe(50),
            () => testProperties["coveringTests"]!["items"]!["type"]!.GetValue<string>().ShouldBe("string"));
    }

    [Fact]
    public void TheReviewSystemPrompt_TellsTheReviewerWhereTheChangeIsAndHowToGradeIt()
    {
        // Arrange
        string prompt = ReadSystemPrompt(WorkerRole.Review.Name);

        // Assert
        prompt.ShouldSatisfyAllConditions(
            () => prompt.ShouldContain("`/handoff/in/review/diff.patch`"),
            () => prompt.ShouldContain("The checkout in your working directory is the reviewed commit."),
            () => prompt.ShouldContain("answer every one in `dimensions`"),
            () => ReviewCategories.ShouldAllBe(category => prompt.Contains($"- `{category}`: ", StringComparison.Ordinal)),
            () => prompt.ShouldContain("Answer every listed acceptance criterion in `criteria` by its `AC` id"),
            () => prompt.ShouldContain("A `blocking` finding in `quality`, `design` or `conventions` must cite a rule"),
            () => prompt.ShouldContain("`design.1`"),
            () => prompt.ShouldContain("`CLAUDE.md`, `AGENTS.md`, `.editorconfig`"),
            () => prompt.ShouldContain("verbatim quote"),
            () => prompt.ShouldContain("A finding without a rule is `advisory`."),
            () => prompt.ShouldContain("by its observable result in the diff"),
            () => prompt.ShouldContain("`foundry-role-review:security-review`"));
    }

    [Fact]
    public void TheReviewSystemPrompt_JudgesEachCriterionAsTheClarificationLogLeavesItOnce()
    {
        // Arrange
        string prompt = ReadSystemPrompt(WorkerRole.Review.Name);

        // Act
        int occurrences = prompt.Split("as the clarification log leaves it").Length - 1;

        // Assert
        prompt.ShouldSatisfyAllConditions(
            () => occurrences.ShouldBe(1),
            () => prompt.ShouldContain("cite the round"),
            () => prompt.ShouldNotContain(SystemPromptBuilder.PersonInputPrecedenceRule));
    }

    [Fact]
    public void TheReviewSecuritySkill_HoldsTheWholeChecklistInOneFile()
    {
        // Arrange
        string skillDirectory = Path.Combine(RolesDirectory, WorkerRole.Review.Name, "skills", "security-review");
        RolePlugin plugin = LoadRepositoryCatalog().Find(WorkerRole.Review).ShouldNotBeNull().Plugin;

        // Act
        string skill = File.ReadAllText(Path.Combine(skillDirectory, "SKILL.md"));

        // Assert
        skill.ShouldSatisfyAllConditions(
            () => plugin.Skills.Select(component => component.Name).ShouldBe(["security-review"]),
            () => Directory.GetFileSystemEntries(skillDirectory).Select(Path.GetFileName).ShouldBe(["SKILL.md"]),
            () => skill.ShouldContain("## Injection"),
            () => skill.ShouldContain("## Authentication and authorization"),
            () => skill.ShouldContain("## Secrets"),
            () => skill.ShouldContain("## Input validation at boundaries"),
            () => skill.ShouldContain("## Cryptography"),
            () => skill.ShouldContain("## Unsafe deserialization"),
            () => skill.ShouldContain("## Path traversal"),
            () => skill.ShouldContain("## Server-side request forgery"));
    }

    [Fact]
    public void WhenTheReviewRolePromptIsRendered_CarriesTheImplementPrinciplesWithTheirIds()
    {
        // Arrange
        WorkerRoleCatalogContent catalog = LoadRepositoryCatalog();
        WorkerRoleDefinition review = catalog.Find(WorkerRole.Review).ShouldNotBeNull();

        // Act
        Result<string> result = SystemPromptBuilder.BuildRestricted(
            catalog.PrinciplesFor(WorkerRole.Review),
            42,
            review.SystemPromptTemplate,
            new IssueContent("Add retry", "Retry webhook delivery.", [], SpecHashFixtures.Any),
            toolchain: ToolchainFixtures.With(csharpLs: false));

        // Assert
        string prompt = result.ShouldBeOfType<Result<string>.Success>().Value;
        prompt.ShouldSatisfyAllConditions(
            () => prompt.ShouldContain(catalog.PrinciplesFor(WorkerRole.Implement)),
            () => catalog.Principles.IdsFor(WorkerRole.Review.PrincipleSections)
                .ShouldAllBe(id => prompt.Contains($"- [{id}] ", StringComparison.Ordinal)));
    }

    [Fact]
    public void EveryRepositoryPrincipleBullet_RendersWithItsSectionIdAndPosition()
    {
        // Arrange
        WorkerRoleCatalogContent catalog = LoadRepositoryCatalog();

        // Act
        string all = catalog.Principles.AssembleAll();

        // Assert
        PrincipleSectionNames.All.ShouldAllBe(section =>
            catalog.Principles.TextOf(section).Split('\n').Count(line => line.StartsWith("- ", StringComparison.Ordinal))
                == all.Split('\n').Count(line => line.StartsWith($"- [{section.ToIdPrefix()}.", StringComparison.Ordinal)));
    }

    [Fact]
    public void TheReviewVerdictSchema_MatchesTheTypedReviewVerdict()
    {
        // Arrange
        JsonObject schema = ReadReviewSchema();

        // Act
        JsonNode properties = schema["properties"]!;
        JsonNode finding = properties[ReviewVerdict.FindingsProperty]!["items"]!;
        JsonNode findingProperties = finding["properties"]!;
        JsonNode dimension = schema["$defs"]!["dimension"]!;
        JsonNode criterion = properties[ReviewVerdict.CriteriaProperty]!["items"]!;
        JsonNode tests = properties[ReviewVerdict.TestsProperty]!;

        // Assert
        schema.ShouldSatisfyAllConditions(
            () => properties.AsObject().Select(pair => pair.Key).ShouldBe(ReviewVerdict.TopLevelProperties, ignoreOrder: true),
            () => findingProperties.AsObject().Select(pair => pair.Key).ShouldBe(ReviewVerdict.FindingProperties, ignoreOrder: true),
            () => findingProperties[ReviewVerdict.RuleProperty]!["properties"]!.AsObject().Select(pair => pair.Key)
                .ShouldBe(ReviewVerdict.RuleProperties, ignoreOrder: true),
            () => StringsOf(findingProperties[ReviewVerdict.SeverityProperty]!["enum"]).ShouldBe(ReviewVerdict.SeverityTokens.Keys, ignoreOrder: true),
            () => StringsOf(findingProperties[ReviewVerdict.CategoryProperty]!["enum"])
                .ShouldBe(ReviewVerdict.CategoryTokens.Select(pair => pair.Key)),
            () => properties[ReviewVerdict.DimensionsProperty]!["properties"]!.AsObject().Select(pair => pair.Key)
                .ShouldBe(ReviewVerdict.CategoryTokens.Select(pair => pair.Key), ignoreOrder: true),
            () => dimension["properties"]!.AsObject().Select(pair => pair.Key).ShouldBe(ReviewVerdict.DimensionProperties, ignoreOrder: true),
            () => StringsOf(dimension["properties"]![ReviewVerdict.StatusProperty]!["enum"])
                .ShouldBe(ReviewVerdict.DimensionStatusTokens.Keys, ignoreOrder: true),
            () => criterion["properties"]!.AsObject().Select(pair => pair.Key).ShouldBe(ReviewVerdict.CriterionProperties, ignoreOrder: true),
            () => criterion["properties"]![ReviewVerdict.IdProperty]!["pattern"]!.GetValue<string>().ShouldBe(ReviewVerdict.CriterionIdPattern),
            () => StringsOf(criterion["properties"]![ReviewVerdict.StatusProperty]!["enum"])
                .ShouldBe(ReviewVerdict.CriterionStatusTokens.Keys, ignoreOrder: true),
            () => tests["properties"]!.AsObject().Select(pair => pair.Key).ShouldBe(ReviewVerdict.TestsProperties, ignoreOrder: true),
            () => StringsOf(tests["properties"]![ReviewVerdict.AssessmentProperty]!["enum"])
                .ShouldBe(ReviewVerdict.AssessmentTokens.Keys, ignoreOrder: true));
    }

    [Theory]
    [InlineData("""{"summary":"Adds retry.","findings":[{"path":"src/a.cs","line":4,"severity":"blocking","category":"design","rule":{"principle":"design.1"},"body":"Stringly typed."},{"path":"src/b.cs","severity":"advisory","category":"quality","body":"Long method."}],"dimensions":{"correctness":{"status":"clear","note":"Read it."},"criteria":{"status":"clear","note":"All met."},"tests":{"status":"clear","note":"Covered."},"security":{"status":"not_applicable","note":"No input."},"quality":{"status":"findings","note":"One."},"design":{"status":"findings","note":"One."},"conventions":{"status":"clear","note":"Fine."}},"criteria":[{"id":"AC1","status":"met","evidence":"Test X."}],"tests":{"assessment":"adequate","reason":"Covered.","coveringTests":["tests/a_test.go"]}}""", RunVerdictStatus.Done)]
    [InlineData("""{"summary":"Adds retry.","findings":[],"dimensions":{},"criteria":[],"tests":{"assessment":"adequate","reason":"","coveringTests":[]}}""", RunVerdictStatus.Invalid)]
    public void ASampleMatchingTheReviewVerdictSchema_ParsesWithTheReviewVerdictParser(string sample, RunVerdictStatus expected)
    {
        // Act
        RunVerdict verdict = new ReviewVerdictParser().Parse(JsonNode.Parse(sample)!.AsObject());

        // Assert
        verdict.Status.ShouldBe(expected);
    }

    private static JsonObject ReadReviewSchema() =>
        ReadJsonObject(Path.Combine(RolesDirectory, WorkerRole.Review.Name, WorkerRoleCatalogReader.VerdictSchemaPath));

    private static string[] StringsOf(JsonNode? array) =>
        [.. array.ShouldNotBeNull().AsArray().Select(node => node!.GetValue<string>())];

    private static void CollectOpenObjects(JsonNode? node, string path, List<string> open)
    {
        switch (node)
        {
            case JsonObject obj:
                bool isObjectSchema = obj["type"] is JsonValue type && type.GetValueKind() == JsonValueKind.String
                    && type.GetValue<string>() == "object";
                bool closed = obj["additionalProperties"] is JsonValue additional
                    && additional.GetValueKind() == JsonValueKind.False;

                if (isObjectSchema && !closed)
                {
                    open.Add(path);
                }

                foreach (KeyValuePair<string, JsonNode?> property in obj)
                {
                    CollectOpenObjects(property.Value, $"{path}.{property.Key}", open);
                }

                break;
            case JsonArray array:
                for (int index = 0; index < array.Count; index++)
                {
                    CollectOpenObjects(array[index], $"{path}[{index}]", open);
                }

                break;
        }
    }

    private static IEnumerable<string> ForbiddenTopLevelKeywordsIn(WorkerRoleDefinition role)
    {
        if (role.VerdictSchema is not { } verdictSchema)
        {
            return [];
        }

        JsonObject schema = JsonNode.Parse(verdictSchema).ShouldNotBeNull().AsObject();

        return ForbiddenTopLevelKeywords
            .Where(schema.ContainsKey)
            .Select(keyword => $"{role.Role.Name}: {keyword}");
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
            new IssueContent("Add retry", "Retry webhook delivery.", [], SpecHashFixtures.Any),
            timeoutMinutes: 120,
            toolchain: ToolchainFixtures.With(docker: false, csharpLs: false));

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
    public void TheFramingFile_OpensWithTheFramingLine()
    {
        // Arrange
        WorkerRoleCatalogContent catalog = LoadRepositoryCatalog();

        // Act
        string framing = catalog.Principles.Framing;

        // Assert
        framing.ShouldStartWith(
            "Engineering principles. The repository's own style, testing and design conventions win where they differ; the safety rules above always apply.\n");
    }

    [Fact]
    public void TheFramingFile_SaysPrivatePackageSourcesRestoreThroughFoundry()
    {
        // Arrange
        WorkerRoleCatalogContent catalog = LoadRepositoryCatalog();

        // Act
        string framing = catalog.Principles.Framing;

        // Assert
        framing.ShouldSatisfyAllConditions(
            () => framing.ShouldContain("Private package sources restore through Foundry"),
            () => framing.ShouldContain("never edit `nuget.config` to fix a restore failure"),
            () => framing.ShouldContain("`dotnet tool restore` from a private package source is not supported in this worker"));
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
            new IssueContent("Add retry", "Retry webhook delivery.", [], SpecHashFixtures.Any),
            toolchain: ToolchainFixtures.With(csharpLs: false));

        // Assert
        string prompt = result.ShouldBeOfType<Result<string>.Success>().Value;
        prompt.ShouldSatisfyAllConditions(
            () => prompt.ShouldContain(catalog.Principles.Framing + "\n\nDesign:\n- [design.1] "),
            () => prompt.ShouldContain("\n- [domain.1] "),
            () => prompt.ShouldNotContain("[testing."),
            () => prompt.ShouldNotContain("[code."),
            () => prompt.IndexOf(principles, StringComparison.Ordinal)
                .ShouldBeLessThan(prompt.IndexOf(judgeLine, StringComparison.Ordinal)));
    }

    [Fact]
    public void TheTriageSystemPrompt_PointsAtTheSuppliedIssuesForPrecedentAndQuestions()
    {
        // Arrange
        string prompt = ReadSystemPrompt(WorkerRole.Triage.Name);

        // Act
        string[] lines = prompt.Split('\n');

        // Assert
        lines.ShouldSatisfyAllConditions(
            () => lines.ShouldContain("- Use the referenced issues and the open-issue list for related work and precedent."),
            () => lines.ShouldContain(
                "- Ask only what the code, its docs and the supplied issues leave open: intent, priority or a product choice."));
    }

    [Fact]
    public void TheTriageSystemPrompt_ReturnsBlockedWhenTheWorkNeedsAnOpenIssueFirst()
    {
        // Arrange
        string prompt = ReadSystemPrompt(WorkerRole.Triage.Name);
        const string AcceptanceSentence =
            "When the work needs an open issue to land first (a referenced issue with `state=\"open\"`, or an entry in the open-issue list), "
            + "return `blocked` and name each such issue with the reason the work needs it. Judge readiness only when nothing it needs is open.";
        const string BlockerLimits =
            "Name only issues from the referenced issues and the open-issue list shown above, each once, at most 50, each with a short reason.";

        // Act
        string[] paragraphs = prompt.Split("\n\n");

        // Assert
        paragraphs.ShouldSatisfyAllConditions(
            () => paragraphs.ShouldContain(AcceptanceSentence),
            () => paragraphs.ShouldContain(BlockerLimits),
            () => Array.IndexOf(paragraphs, AcceptanceSentence).ShouldBe(
                Array.FindIndex(paragraphs, paragraph => paragraph.StartsWith("Foundry holds this issue until", StringComparison.Ordinal)) + 1));
    }

    [Theory]
    [InlineData("triage")]
    [InlineData("implement")]
    public void RoleSystemPrompt_StatesFoundryHoldsTheIssueUntilItsBlockersClose(string roleName)
    {
        // Arrange
        const string statement =
            "Foundry holds this issue until its `blocked_by` issues close. Read another issue's state and its closing merge request from the referenced issues. Never ask whether another issue has merged, or whether to wait for it.";

        // Act
        string prompt = ReadSystemPrompt(roleName);

        // Assert
        prompt.ShouldContain(statement);
    }

    [Fact]
    public void TheImplementSystemPrompt_NamesTheReferencedIssuesAndPointsAtThemForPrecedent()
    {
        // Arrange
        string prompt = ReadSystemPrompt(WorkerRole.Implement.Name);

        // Act
        string[] lines = prompt.Split('\n');

        // Assert
        lines.ShouldSatisfyAllConditions(
            () => lines[0].ShouldContain("Foundry read the issue's title, body and comments, and the issues it references, when it dispatched this run"),
            () => lines.ShouldContain("Use the referenced issues and the open-issue list for related work and precedent."));
    }

    private static string ReadSystemPrompt(string roleName) =>
        File.ReadAllText(Path.Combine(RolesDirectory, roleName, WorkerRoleCatalogReader.SystemPromptPath))
            .Replace("\r\n", "\n", StringComparison.Ordinal);

    private static WorkerRoleCatalogContent LoadRepositoryCatalog() =>
        WorkerRoleCatalogReader.ReadFromDirectory(RepositoryRoot.WorkersDirectory)
            .ShouldBeOfType<Result<WorkerRoleCatalogContent>.Success>().Value;
}
