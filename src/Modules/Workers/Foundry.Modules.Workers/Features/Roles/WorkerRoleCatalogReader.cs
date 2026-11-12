using System.Formats.Tar;
using System.Text;
using System.Text.Json;

using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Shared;

namespace Foundry.Modules.Workers.Features.Roles;

/// <summary>
/// Reads the role catalog (each role's plugin manifest, skills, agents, prompts and verdict schema) from the worker
/// build context: from the build-context tar during an image build, or from disk when image build is disabled.
/// </summary>
internal static class WorkerRoleCatalogReader
{
    /// <summary>The roles folder, relative to the <c>workers/</c> build context.</summary>
    internal const string RolesFolder = "roles";

    internal const string PluginManifestPath = ".claude-plugin/plugin.json";
    internal const string SystemPromptPath = "prompts/system.md";
    internal const string TaskPromptPath = "prompts/task.md";
    internal const string VerdictSchemaPath = "verdict.schema.json";

    /// <summary>The <c>$defs</c> every role's verdict schema may reference (e.g. <c>#/$defs/questions</c>), under <c>shared/</c>.</summary>
    internal const string SharedVerdictDefinitionsPath = "verdict.defs.json";

    /// <summary>The Worker Principles framing line, under <c>shared/</c>: opens every non-empty principles block.</summary>
    internal const string PrinciplesPath = "prompts/principles.md";

    /// <summary>The folder of the Principle Section files, under <c>shared/</c>: one file per section.</summary>
    internal const string PrincipleSectionsFolder = "prompts/principles";

    // Role files are small text; the cap keeps a stray large file from being held in memory for the API.
    internal const int MaxFileBytes = 256 * 1024;

    // The principles ride in the system prompt of every role that selects a section, so a cap on the framing with
    // every section, far below SystemPromptBuilder.MaxSystemPromptBytes, fails oversized files at parse time instead
    // of failing every dispatch.
    internal const int MaxPrinciplesBytes = 8 * 1024;

    internal const string InvalidCatalogCode = "Worker.RoleCatalogInvalid";

    private const string SkillsFolder = "skills/";
    private const string SkillFileName = "SKILL.md";
    private const string AgentsFolder = "agents/";
    private const string FrontmatterFence = "---";

    // The builder fills placeholders in role templates only, so any placeholder in the principles would reach the worker literally.
    private static readonly IReadOnlyList<string> NoPlaceholders = [];

    private static readonly EnumerationOptions IncludeHiddenEntries = new()
    {
        RecurseSubdirectories = true,
        AttributesToSkip = 0,
    };

    /// <summary>
    /// Reads the catalog from a build-context tar positioned at its start. The stream is left open; the caller
    /// rewinds it before handing the same bytes to the image build.
    /// </summary>
    public static async Task<Result<WorkerRoleCatalogContent>> ReadFromTarAsync(Stream tar, CancellationToken cancellationToken)
    {
        Dictionary<string, string> files = new(StringComparer.Ordinal);
        TarReader reader = new(tar, leaveOpen: true);

        await using (reader.ConfigureAwait(false))
        {
            string prefix = RolesFolder + "/";

            while (await reader.GetNextEntryAsync(copyData: false, cancellationToken) is { } entry)
            {
                if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile)
                    || !entry.Name.StartsWith(prefix, StringComparison.Ordinal))
                {
                    continue;
                }

                string relativePath = entry.Name[prefix.Length..];

                if (entry.Length > MaxFileBytes)
                {
                    return Fail($"'{RolesFolder}/{relativePath}' is larger than {MaxFileBytes} bytes.");
                }

                if (entry.DataStream is null)
                {
                    files[relativePath] = string.Empty;
                    continue;
                }

                using StreamReader text = new(entry.DataStream, Encoding.UTF8, leaveOpen: true);
                files[relativePath] = await text.ReadToEndAsync(cancellationToken);
            }
        }

        return Parse(files);
    }

    /// <summary>Reads the catalog from the <c>roles/</c> folder of the build context on disk.</summary>
    public static Result<WorkerRoleCatalogContent> ReadFromDirectory(string contextDirectory)
    {
        string rolesDirectory = Path.Combine(contextDirectory, RolesFolder);

        if (!Directory.Exists(rolesDirectory))
        {
            return Fail($"'{RolesFolder}/' does not exist in the worker build context.");
        }

        Dictionary<string, string> files = new(StringComparer.Ordinal);

        foreach (string path in Directory.EnumerateFiles(rolesDirectory, "*", IncludeHiddenEntries))
        {
            string relativePath = Path.GetRelativePath(rolesDirectory, path).Replace('\\', '/');

            if (new FileInfo(path).Length > MaxFileBytes)
            {
                return Fail($"'{RolesFolder}/{relativePath}' is larger than {MaxFileBytes} bytes.");
            }

            files[relativePath] = File.ReadAllText(path, Encoding.UTF8);
        }

        return Parse(files);
    }

    /// <summary>Builds the catalog from the files under <c>roles/</c>, keyed by their path relative to it.</summary>
    internal static Result<WorkerRoleCatalogContent> Parse(IReadOnlyDictionary<string, string> files)
    {
        Result<RolePlugin> sharedResult = ReadPlugin(files, WorkerRole.SharedFolder);

        if (sharedResult is not Result<RolePlugin>.Success shared)
        {
            return Result<WorkerRoleCatalogContent>.Fail(((Result<RolePlugin>.Failure)sharedResult).Error);
        }

        string sharedVerdictDefinitions = $"{WorkerRole.SharedFolder}/{SharedVerdictDefinitionsPath}";

        if (files.TryGetValue(sharedVerdictDefinitions, out string? definitions)
            && CheckJsonObject(sharedVerdictDefinitions, definitions) is { } definitionsError)
        {
            return Fail(definitionsError);
        }

        Result<WorkerPrinciples> principlesResult = ReadPrinciples(files);

        if (principlesResult is not Result<WorkerPrinciples>.Success principles)
        {
            return Result<WorkerRoleCatalogContent>.Fail(((Result<WorkerPrinciples>.Failure)principlesResult).Error);
        }

        List<WorkerRoleDefinition> roles = [];

        foreach (WorkerRole role in WorkerRole.All)
        {
            Result<WorkerRoleDefinition> roleResult = ReadRole(files, role);

            if (roleResult is not Result<WorkerRoleDefinition>.Success definition)
            {
                return Result<WorkerRoleCatalogContent>.Fail(((Result<WorkerRoleDefinition>.Failure)roleResult).Error);
            }

            roles.Add(definition.Value);
        }

        return Result<WorkerRoleCatalogContent>.Ok(new WorkerRoleCatalogContent(roles, shared.Value, principles.Value));
    }

    /// <summary>
    /// Reads the framing file and every Principle Section file. Each must be present, non-blank and free of
    /// placeholders, and the framing with every section, the largest text a role can receive, stays under the cap.
    /// </summary>
    private static Result<WorkerPrinciples> ReadPrinciples(IReadOnlyDictionary<string, string> files)
    {
        Result<string> framingResult = ReadPrinciplesFile(files, PrinciplesPath);

        if (framingResult is not Result<string>.Success framing)
        {
            return Result<WorkerPrinciples>.Fail(((Result<string>.Failure)framingResult).Error);
        }

        Dictionary<PrincipleSection, string> sections = [];

        foreach (PrincipleSection section in PrincipleSectionNames.All)
        {
            Result<string> sectionResult = ReadPrinciplesFile(files, PrincipleSectionPath(section));

            if (sectionResult is not Result<string>.Success text)
            {
                return Result<WorkerPrinciples>.Fail(((Result<string>.Failure)sectionResult).Error);
            }

            sections[section] = text.Value;
        }

        WorkerPrinciples principles = new(framing.Value, sections);
        int principlesBytes = Encoding.UTF8.GetByteCount(principles.AssembleAll());

        if (principlesBytes > MaxPrinciplesBytes)
        {
            return Fail<WorkerPrinciples>(
                $"'{RolesFolder}/{WorkerRole.SharedFolder}/{PrinciplesPath}' with every section under "
                + $"'{RolesFolder}/{WorkerRole.SharedFolder}/{PrincipleSectionsFolder}/' is {principlesBytes} bytes, "
                + $"over the {MaxPrinciplesBytes}-byte cap.");
        }

        return Result<WorkerPrinciples>.Ok(principles);
    }

    private static Result<string> ReadPrinciplesFile(IReadOnlyDictionary<string, string> files, string path)
    {
        string file = $"'{RolesFolder}/{WorkerRole.SharedFolder}/{path}'";
        string? text = ReadPrompt(files, WorkerRole.SharedFolder, path);

        if (text is null)
        {
            return Fail<string>($"{file} is missing or blank.");
        }

        return CheckPlaceholders(file, text, NoPlaceholders) is { } placeholderError
            ? Fail<string>(placeholderError)
            : Result<string>.Ok(text);
    }

    /// <summary>The path of <paramref name="section"/>'s file, relative to <c>shared/</c>.</summary>
    internal static string PrincipleSectionPath(PrincipleSection section) =>
        $"{PrincipleSectionsFolder}/{section.ToFileName()}";

    private static Result<WorkerRoleDefinition> ReadRole(IReadOnlyDictionary<string, string> files, WorkerRole role)
    {
        Result<RolePlugin> pluginResult = ReadPlugin(files, role.Name);

        if (pluginResult is not Result<RolePlugin>.Success plugin)
        {
            return Result<WorkerRoleDefinition>.Fail(((Result<RolePlugin>.Failure)pluginResult).Error);
        }

        string? systemPrompt = ReadPrompt(files, role.Name, SystemPromptPath);
        string? taskPrompt = ReadPrompt(files, role.Name, TaskPromptPath);

        if (systemPrompt is null)
        {
            return Fail<WorkerRoleDefinition>($"Role '{role.Name}' has no {RolesFolder}/{role.Name}/{SystemPromptPath}.");
        }

        if (taskPrompt is null)
        {
            return Fail<WorkerRoleDefinition>($"Role '{role.Name}' has no {RolesFolder}/{role.Name}/{TaskPromptPath}.");
        }

        string? placeholderError =
            CheckPlaceholders(
                $"Role '{role.Name}' {SystemPromptPath}",
                systemPrompt,
                RolePromptPlaceholders.SystemPrompt)
            ?? CheckPlaceholders($"Role '{role.Name}' {TaskPromptPath}", taskPrompt, RolePromptPlaceholders.TaskPrompt);

        if (placeholderError is not null)
        {
            return Fail<WorkerRoleDefinition>(placeholderError);
        }

        if (!taskPrompt.Contains(RolePromptPlaceholders.IssueNumber, StringComparison.Ordinal))
        {
            return Fail<WorkerRoleDefinition>(
                $"Role '{role.Name}' task prompt must contain the {RolePromptPlaceholders.IssueNumber} placeholder.");
        }

        files.TryGetValue($"{role.Name}/{VerdictSchemaPath}", out string? verdictSchema);

        if (verdictSchema is not null && CheckJsonObject($"{role.Name}/{VerdictSchemaPath}", verdictSchema) is { } schemaError)
        {
            return Fail<WorkerRoleDefinition>(schemaError);
        }

        return Result<WorkerRoleDefinition>.Ok(new WorkerRoleDefinition(
            role,
            role.Capability,
            plugin.Value,
            systemPrompt,
            taskPrompt,
            verdictSchema is null ? null : Normalize(verdictSchema)));
    }

    /// <summary>
    /// Returns an error when <paramref name="content"/> is not a JSON object. The entrypoint passes a role's verdict
    /// schema to <c>claude --json-schema</c> with the shared definitions merged in, so either file breaking fails the
    /// image build rather than every run of the role.
    /// </summary>
    private static string? CheckJsonObject(string path, string content)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(content);

            return document.RootElement.ValueKind == JsonValueKind.Object
                ? null
                : $"'{RolesFolder}/{path}' is not a JSON object.";
        }
        catch (JsonException)
        {
            return $"'{RolesFolder}/{path}' is not valid JSON.";
        }
    }

    private static string? CheckPlaceholders(string file, string template, IReadOnlyList<string> allowed)
    {
        string[] unknown = [.. RolePromptPlaceholders.FindIn(template).Where(placeholder => !allowed.Contains(placeholder))];

        return unknown.Length == 0
            ? null
            : $"{file} uses placeholders Foundry does not fill: {string.Join(", ", unknown)}.";
    }

    private static string? ReadPrompt(IReadOnlyDictionary<string, string> files, string folder, string promptPath)
    {
        if (!files.TryGetValue($"{folder}/{promptPath}", out string? content))
        {
            return null;
        }

        string normalized = Normalize(content).Trim();
        return normalized.Length == 0 ? null : normalized;
    }

    private static Result<RolePlugin> ReadPlugin(IReadOnlyDictionary<string, string> files, string folder)
    {
        if (!files.TryGetValue($"{folder}/{PluginManifestPath}", out string? manifest))
        {
            return Fail<RolePlugin>($"'{RolesFolder}/{folder}/' has no plugin manifest ({PluginManifestPath}).");
        }

        string? description;

        try
        {
            using JsonDocument document = JsonDocument.Parse(manifest);

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return Fail<RolePlugin>($"'{RolesFolder}/{folder}/{PluginManifestPath}' is not a JSON object.");
            }

            description = document.RootElement.TryGetProperty("description", out JsonElement value)
                && value.ValueKind == JsonValueKind.String
                    ? value.GetString()
                    : null;
        }
        catch (JsonException)
        {
            return Fail<RolePlugin>($"'{RolesFolder}/{folder}/{PluginManifestPath}' is not valid JSON.");
        }

        string folderPrefix = folder + "/";
        List<RolePluginComponent> skills = [];
        List<RolePluginComponent> agents = [];

        foreach ((string path, string content) in files.OrderBy(file => file.Key, StringComparer.Ordinal))
        {
            if (!path.StartsWith(folderPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            string inPlugin = path[folderPrefix.Length..];

            if (inPlugin.StartsWith(SkillsFolder, StringComparison.Ordinal))
            {
                string[] parts = inPlugin[SkillsFolder.Length..].Split('/');

                if (parts is [string skillFolder, SkillFileName])
                {
                    skills.Add(ToComponent(skillFolder, $"{RolesFolder}/{path}", content));
                }
            }
            else if (inPlugin.StartsWith(AgentsFolder, StringComparison.Ordinal))
            {
                string agentFile = inPlugin[AgentsFolder.Length..];

                if (!agentFile.Contains('/', StringComparison.Ordinal)
                    && agentFile.EndsWith(".md", StringComparison.Ordinal))
                {
                    agents.Add(ToComponent(agentFile[..^".md".Length], $"{RolesFolder}/{path}", content));
                }
            }
        }

        return Result<RolePlugin>.Ok(new RolePlugin(folder, description, skills, agents));
    }

    private static RolePluginComponent ToComponent(string fallbackName, string path, string content)
    {
        string normalized = Normalize(content);
        IReadOnlyDictionary<string, string> frontmatter = ReadFrontmatter(normalized);

        string name = frontmatter.TryGetValue("name", out string? declaredName) && declaredName.Length > 0
            ? declaredName
            : fallbackName;

        frontmatter.TryGetValue("description", out string? description);

        return new RolePluginComponent(name, string.IsNullOrEmpty(description) ? null : description, path, normalized);
    }

    /// <summary>Reads the flat <c>key: value</c> pairs of a leading <c>---</c> fenced YAML frontmatter block.</summary>
    internal static IReadOnlyDictionary<string, string> ReadFrontmatter(string content)
    {
        Dictionary<string, string> values = new(StringComparer.Ordinal);
        string[] lines = content.Split('\n');

        if (lines.Length == 0 || lines[0].Trim() != FrontmatterFence)
        {
            return values;
        }

        for (int i = 1; i < lines.Length; i++)
        {
            string line = lines[i];

            if (line.Trim() == FrontmatterFence)
            {
                return values;
            }

            int colon = line.IndexOf(':', StringComparison.Ordinal);

            if (colon <= 0 || char.IsWhiteSpace(line[0]))
            {
                continue;
            }

            string key = line[..colon].Trim();
            string value = line[(colon + 1)..].Trim();

            if (value.Length >= 2 && (value[0] is '"' or '\'') && value[^1] == value[0])
            {
                value = value[1..^1];
            }

            values.TryAdd(key, value);
        }

        // No closing fence: not a frontmatter block.
        return new Dictionary<string, string>(StringComparer.Ordinal);
    }

    private static string Normalize(string content) =>
        content.Replace("\r\n", "\n", StringComparison.Ordinal).TrimStart('\uFEFF');

    private static Result<WorkerRoleCatalogContent> Fail(string message) =>
        Result<WorkerRoleCatalogContent>.Fail(new Error(InvalidCatalogCode, message));

    private static Result<T> Fail<T>(string message)
        where T : notnull =>
        Result<T>.Fail(new Error(InvalidCatalogCode, message));
}
