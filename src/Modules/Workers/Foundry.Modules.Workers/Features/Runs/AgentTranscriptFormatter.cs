using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

using Foundry.Shared.Infrastructure.Docker;

namespace Foundry.Modules.Workers.Features.Runs;

/// <summary>
/// Renders the worker's <c>claude --output-format stream-json</c> log as a readable, summary-only
/// Agent Transcript: one line per meaningful event (tool call with an input summary, tool
/// success/failure with duration, assistant text, subagent start/finish, final result).
/// Tool result bodies are never rendered — only a truncated error message on failure — and
/// noise events (thinking, hooks, rate limits, task progress) are omitted. Non-JSON lines
/// (bootstrap output, stderr, sentinels) pass through unchanged.
/// </summary>
/// <remarks>
/// Stateful: tool-call durations and subagent labels are correlated across lines, so a single
/// instance must see one container log in order. Not thread-safe.
/// </remarks>
internal sealed partial class AgentTranscriptFormatter
{
    private const int MaxCommandLength = 200;
    private const int MaxAssistantTextLength = 1_000;
    private const int MaxErrorLength = 200;
    private const int MaxTrackedEntries = 1_000;

    private static readonly IReadOnlyList<string> NoLines = [];

    private readonly Dictionary<string, PendingTool> _pendingTools = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _subagentLabels = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _taskDescriptions = new(StringComparer.Ordinal);

    /// <summary>Formats a whole captured log (e.g. a stored failed-run log) as a transcript.</summary>
    public static string FormatLog(string log)
    {
        AgentTranscriptFormatter formatter = new();

        return string.Join(
            '\n',
            log.Split('\n').SelectMany(line => formatter.FormatLine(line.TrimEnd('\r'))));
    }

    /// <summary>
    /// Formats one log line. Returns the transcript lines it renders to: none for omitted
    /// events, the line unchanged for non-JSON input.
    /// </summary>
    public IReadOnlyList<string> FormatLine(string line)
    {
        (string prefix, string body) = SplitDockerTimestamp(line);
        string trimmed = body.Trim();

        if (trimmed.Length == 0 || trimmed[0] != '{')
        {
            return [line];
        }

        JsonObject? evt = TryParseObject(trimmed);

        if (evt is null || ReadString(evt["type"]) is not { } type)
        {
            return [line];
        }

        DateTimeOffset? at = ReadTimestamp(evt["timestamp"]) ?? ParseDockerTimestamp(prefix);
        string indent = SubagentIndent(evt);

        List<string> rendered = type switch
        {
            "assistant" => FormatAssistant(evt, at),
            "user" => FormatUser(evt, at),
            "system" => FormatSystem(evt),
            "result" => [FormatResult(evt)],
            _ => [],
        };

        if (rendered.Count == 0)
        {
            return NoLines;
        }

        // Source lines are redacted before they reach the formatter; redact again because
        // JSON unescaping can surface a secret shape the escaped form did not match.
        return rendered
            .Select(text => prefix + indent + SecretRedactor.Redact(text))
            .ToList();
    }

    private List<string> FormatAssistant(JsonObject evt, DateTimeOffset? at)
    {
        List<string> lines = [];

        foreach (JsonObject block in ContentBlocks(evt))
        {
            switch (ReadString(block["type"]))
            {
                case "text":
                    string text = Collapse(ReadString(block["text"]), MaxAssistantTextLength);

                    if (text.Length > 0)
                    {
                        lines.Add($"Assistant: {text}");
                    }

                    break;

                case "tool_use":
                    lines.Add(FormatToolUse(block, at));
                    break;
            }
        }

        return lines;
    }

    private string FormatToolUse(JsonObject block, DateTimeOffset? at)
    {
        string name = ReadString(block["name"]) ?? "tool";
        JsonNode? input = block["input"];

        if (ReadString(block["id"]) is { } id)
        {
            Track(_pendingTools, id, new PendingTool(name, at));

            if (IsAgentTool(name))
            {
                Track(_subagentLabels, id, ReadString(input?["subagent_type"]) ?? "subagent");
            }
        }

        string? summary = SummarizeInput(name, input);

        return summary is null ? $"→ {name}" : $"→ {name}: {summary}";
    }

    internal static string? SummarizeInput(string name, JsonNode? input)
    {
        if (input is not JsonObject)
        {
            return null;
        }

        string? summary = name switch
        {
            "Bash" => Collapse(ReadString(input["command"]), MaxCommandLength),
            "Read" or "Edit" or "Write" or "MultiEdit" or "NotebookEdit" =>
                ReadString(input["file_path"]) ?? ReadString(input["notebook_path"]),
            "Agent" or "Task" => JoinNonEmpty(
                ReadString(input["subagent_type"]),
                Collapse(ReadString(input["description"]), MaxCommandLength)),
            "Skill" => ReadString(input["skill"]),
            "Grep" or "Glob" => Collapse(ReadString(input["pattern"]), MaxCommandLength),
            "WebFetch" => ReadString(input["url"]),
            "WebSearch" => Collapse(ReadString(input["query"]), MaxCommandLength),
            _ => null,
        };

        return string.IsNullOrWhiteSpace(summary) ? null : summary;
    }

    private List<string> FormatUser(JsonObject evt, DateTimeOffset? at)
    {
        List<string> lines = [];

        foreach (JsonObject block in ContentBlocks(evt))
        {
            if (!string.Equals(ReadString(block["type"]), "tool_result", StringComparison.Ordinal))
            {
                continue;
            }

            string? toolUseId = ReadString(block["tool_use_id"]);
            PendingTool? pending = null;

            if (toolUseId is not null && _pendingTools.Remove(toolUseId, out PendingTool found))
            {
                pending = found;
            }

            string name = pending?.Name ?? "tool";
            string duration = pending?.StartedAt is { } start && at is { } end && end >= start
                ? $" ({FormatDuration(end - start)})"
                : string.Empty;

            if (block["is_error"]?.GetValueKind() == JsonValueKind.True)
            {
                string error = Collapse(ReadToolResultText(block["content"]), MaxErrorLength);
                lines.Add(error.Length > 0
                    ? $"✗ {name} failed{duration}: {error}"
                    : $"✗ {name} failed{duration}");
            }
            else
            {
                lines.Add($"✓ {name}{duration}");
            }
        }

        return lines;
    }

    private List<string> FormatSystem(JsonObject evt)
    {
        switch (ReadString(evt["subtype"]))
        {
            case "init":
                string? model = ReadString(evt["model"]);
                return [model is null ? "Session started" : $"Session started (model {model})"];

            case "task_started":
                string? description = ReadString(evt["description"]);

                if (ReadString(evt["task_id"]) is { } taskId && description is not null)
                {
                    Track(_taskDescriptions, taskId, description);
                }

                string? started = JoinNonEmpty(
                    ReadString(evt["subagent_type"]),
                    Collapse(description, MaxCommandLength));

                return [started is null ? "Subagent started" : $"Subagent started: {started}"];

            case "task_notification":
                string status = ReadString(evt["status"]) ?? "finished";
                string? finishedDescription = ReadString(evt["task_id"]) is { } finishedTaskId
                    && _taskDescriptions.Remove(finishedTaskId, out string? known)
                        ? Collapse(known, MaxCommandLength)
                        : null;

                return [finishedDescription is null
                    ? $"Subagent {status}"
                    : $"Subagent {status}: {finishedDescription}"];

            default:
                // thinking_tokens, hook_*, task_progress, task_updated and unknown subtypes.
                return [];
        }
    }

    private static string FormatResult(JsonObject evt)
    {
        string subtype = ReadString(evt["subtype"]) ?? "unknown";
        bool isError = evt["is_error"]?.GetValueKind() == JsonValueKind.True;
        List<string> parts = [isError ? $"Result: error ({subtype})" : $"Result: {subtype}"];

        if (ReadNumber(evt["num_turns"]) is { } turns)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"{turns:0} turns"));
        }

        if (ReadNumber(evt["duration_ms"]) is { } durationMs)
        {
            parts.Add(FormatDuration(TimeSpan.FromMilliseconds((double)durationMs)));
        }

        if (ReadNumber(evt["total_cost_usd"]) is { } cost)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"${cost:0.00}"));
        }

        return string.Join(" · ", parts);
    }

    private string SubagentIndent(JsonObject evt)
    {
        if (ReadString(evt["parent_tool_use_id"]) is not { } parentId)
        {
            return string.Empty;
        }

        string label = ReadString(evt["subagent_type"])
            ?? (_subagentLabels.TryGetValue(parentId, out string? known) ? known : "subagent");

        return $"  [{label}] ";
    }

    internal static IEnumerable<JsonObject> ContentBlocks(JsonObject evt)
    {
        return evt["message"]?["content"] is JsonArray content
            ? content.OfType<JsonObject>()
            : [];
    }

    internal static string? ReadToolResultText(JsonNode? content)
    {
        return content switch
        {
            JsonValue value when value.GetValueKind() == JsonValueKind.String => value.GetValue<string>(),
            JsonArray blocks => string.Join(
                ' ',
                blocks.OfType<JsonObject>().Select(b => ReadString(b["text"])).OfType<string>()),
            _ => null,
        };
    }

    internal static bool IsAgentTool(string name)
    {
        return name is "Agent" or "Task";
    }

    private static void Track<T>(Dictionary<string, T> map, string key, T value)
    {
        // Bound the correlation state: a run killed mid-tool leaves entries that never resolve.
        if (map.Count >= MaxTrackedEntries)
        {
            map.Clear();
        }

        map[key] = value;
    }

    private static string FormatDuration(TimeSpan duration)
    {
        return duration.TotalMinutes >= 1
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"{(int)duration.TotalMinutes}m {duration.Seconds:00}s")
            : string.Create(CultureInfo.InvariantCulture, $"{duration.TotalSeconds:0.0}s");
    }

    private static string? JoinNonEmpty(string? first, string? second)
    {
        string[] parts = new[] { first, second }
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!)
            .ToArray();

        return parts.Length == 0 ? null : string.Join(" — ", parts);
    }

    internal static string Collapse(string? text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        string collapsed = WhitespaceRunPattern().Replace(text, " ").Trim();

        return collapsed.Length > maxLength
            ? string.Concat(collapsed.AsSpan(0, maxLength), "…")
            : collapsed;
    }

    internal static JsonObject? TryParseObject(string json)
    {
        try
        {
            return JsonNode.Parse(json) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal static string? ReadString(JsonNode? node)
    {
        return node is JsonValue value && value.GetValueKind() == JsonValueKind.String
            ? value.GetValue<string>()
            : null;
    }

    internal static decimal? ReadNumber(JsonNode? node)
    {
        return node is JsonValue value
            && value.GetValueKind() == JsonValueKind.Number
            && value.TryGetValue(out decimal number)
                ? number
                : null;
    }

    internal static DateTimeOffset? ReadTimestamp(JsonNode? node)
    {
        return ReadString(node) is { } text
            && DateTimeOffset.TryParse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out DateTimeOffset parsed)
                ? parsed
                : null;
    }

    internal static DateTimeOffset? ParseDockerTimestamp(string prefix)
    {
        return prefix.Length > 0
            && DateTimeOffset.TryParse(
                prefix.Trim(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out DateTimeOffset parsed)
                ? parsed
                : null;
    }

    internal static (string Prefix, string Body) SplitDockerTimestamp(string line)
    {
        Match match = DockerTimestampPrefixPattern().Match(line);

        return match.Success
            ? (line[..match.Length], line[match.Length..])
            : (string.Empty, line);
    }

    private readonly record struct PendingTool(string Name, DateTimeOffset? StartedAt);

    // Matches a Docker log timestamp prefix: RFC3339Nano date-time followed by a space.
    [GeneratedRegex(
        @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:\d{2}) ",
        RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex DockerTimestampPrefixPattern();

    [GeneratedRegex(@"\s+", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex WhitespaceRunPattern();
}
