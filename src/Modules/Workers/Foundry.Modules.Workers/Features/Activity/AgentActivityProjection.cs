using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Features.Runs;
using Foundry.Shared.Infrastructure.Docker;

namespace Foundry.Modules.Workers.Features.Activity;

/// <summary>
/// Folds a worker's <c>claude --output-format stream-json</c> log into the Agent Activity
/// projection: the agent's task list, active subagents and skill, the in-flight tool, token
/// totals and <c>LastEventAt</c>. Workflow-agnostic — it reads the agent's own signals.
/// </summary>
/// <remarks>
/// Stateful: tool calls, task ids and subagents are correlated across lines, so a single
/// instance must see one container log in order. A repeated <c>system/init</c> (the
/// <c>claude -c</c> transient retry) continues the projection rather than resetting it.
/// Not thread-safe.
/// </remarks>
internal sealed partial class AgentActivityProjection
{
    private const int MaxSummaryLength = 200;
    private const int MaxTrackedEntries = 1_000;
    private const int MaxTrackedMessageIds = 20_000;
    private const int MaxTasks = 200;

    private readonly List<TaskEntry> _tasks = [];
    private readonly Dictionary<string, PendingTool> _pendingTools = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SubagentEntry> _subagents = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _subagentKeyByTaskId = new(StringComparer.Ordinal);
    private readonly HashSet<string> _countedMessageIds = new(StringComparer.Ordinal);

    private long _sequence;
    private string? _activeSkill;
    private long _inputTokens;
    private long _outputTokens;
    private long _cacheReadInputTokens;
    private long _cacheCreationInputTokens;

    public DateTimeOffset? LastEventAt { get; private set; }

    /// <summary>
    /// Applies one container log line. Returns <see langword="true"/> when the line was a
    /// stream-json event (and so may have changed the projection); non-JSON lines are ignored.
    /// </summary>
    public bool Apply(string line, DateTimeOffset receivedAt)
    {
        (string prefix, string body) = AgentTranscriptFormatter.SplitDockerTimestamp(line);
        string trimmed = body.Trim();

        if (trimmed.Length == 0 || trimmed[0] != '{')
        {
            return false;
        }

        JsonObject? evt = AgentTranscriptFormatter.TryParseObject(trimmed);

        if (evt is null || ReadString(evt["type"]) is not { } type)
        {
            return false;
        }

        DateTimeOffset at = AgentTranscriptFormatter.ReadTimestamp(evt["timestamp"])
            ?? AgentTranscriptFormatter.ParseDockerTimestamp(prefix)
            ?? receivedAt;

        if (LastEventAt is null || at > LastEventAt)
        {
            LastEventAt = at;
        }

        switch (type)
        {
            case "assistant":
                ApplyAssistant(evt, at);
                break;

            case "user":
                ApplyUser(evt);
                break;

            case "system":
                ApplySystem(evt);
                break;

            case "result":
                // The session ended; anything still pending will never resolve. A claude -c
                // retry that follows starts fresh tool calls but keeps tasks and token totals.
                _pendingTools.Clear();
                _subagents.Clear();
                _subagentKeyByTaskId.Clear();
                _activeSkill = null;
                break;
        }

        return true;
    }

    public AgentActivity Snapshot(WorkerRunId workerRunId, Guid issueId)
    {
        List<AgentTaskItem> tasks = _tasks.Select(t => t.ToItem()).ToList();

        AgentTaskItem? currentTask = _tasks
            .Where(t => t.Status == "in_progress")
            .OrderByDescending(t => t.UpdatedSequence)
            .Select(t => t.ToItem())
            .FirstOrDefault();

        List<ActiveSubagent> subagents = _subagents.Values
            .OrderBy(s => s.Sequence)
            .Select(s => new ActiveSubagent(s.SubagentType, s.Description, s.SpawnDepth, s.LastTool))
            .ToList();

        // Prefer an ordinary tool over the Agent call that is pending for a subagent's whole
        // lifetime: subagents are shown separately, the tool they are running is more useful.
        InFlightTool? inFlightTool = _pendingTools.Values
            .OrderBy(t => AgentTranscriptFormatter.IsAgentTool(t.Name) ? 0 : 1)
            .ThenBy(t => t.Sequence)
            .Select(t => new InFlightTool(t.Name, t.Summary, t.StartedAt))
            .LastOrDefault();

        return new AgentActivity(
            WorkerRunId: workerRunId,
            IssueId: issueId,
            LastEventAt: LastEventAt,
            Tasks: tasks,
            CurrentTask: currentTask,
            CompletedTaskCount: _tasks.Count(t => t.Status == "completed"),
            Subagents: subagents,
            ActiveSkill: _activeSkill,
            InFlightTool: inFlightTool,
            Tokens: new AgentTokenTotals(
                _inputTokens,
                _outputTokens,
                _cacheReadInputTokens,
                _cacheCreationInputTokens));
    }

    private void ApplyAssistant(JsonObject evt, DateTimeOffset at)
    {
        CountUsage(evt["message"]);

        string? parentToolUseId = ReadString(evt["parent_tool_use_id"]);

        foreach (JsonObject block in AgentTranscriptFormatter.ContentBlocks(evt))
        {
            if (ReadString(block["type"]) == "tool_use")
            {
                ApplyToolUse(block, parentToolUseId, at);
            }
        }
    }

    private void CountUsage(JsonNode? message)
    {
        // Each message is emitted once per content block with identical usage, so count it once.
        if (message?["usage"] is not JsonObject usage
            || ReadString(message["id"]) is not { } messageId)
        {
            return;
        }

        if (_countedMessageIds.Count >= MaxTrackedMessageIds)
        {
            _countedMessageIds.Clear();
        }

        if (!_countedMessageIds.Add(messageId))
        {
            return;
        }

        _inputTokens += ReadLong(usage["input_tokens"]);
        _outputTokens += ReadLong(usage["output_tokens"]);
        _cacheReadInputTokens += ReadLong(usage["cache_read_input_tokens"]);
        _cacheCreationInputTokens += ReadLong(usage["cache_creation_input_tokens"]);
    }

    private void ApplyToolUse(JsonObject block, string? parentToolUseId, DateTimeOffset at)
    {
        string name = ReadString(block["name"]) ?? "tool";
        JsonNode? input = block["input"];
        string? id = ReadString(block["id"]);
        long sequence = ++_sequence;

        string? summary = AgentTranscriptFormatter.SummarizeInput(name, input) is { } raw
            ? Redact(raw)
            : null;

        if (id is not null)
        {
            Track(_pendingTools, id, new PendingTool(name, summary, at, sequence, input as JsonObject));
        }

        if (parentToolUseId is not null && _subagents.TryGetValue(parentToolUseId, out SubagentEntry? parent))
        {
            parent.LastTool = name;
        }

        switch (name)
        {
            case "TaskUpdate":
                ApplyTaskUpdate(input, sequence);
                break;

            case "Skill":
                _activeSkill = Redact(ReadString(input?["skill"])) ?? _activeSkill;
                break;

            case "Agent" or "Task" when id is not null:
                int depth = parentToolUseId is not null && _subagents.TryGetValue(parentToolUseId, out SubagentEntry? spawner)
                    ? spawner.SpawnDepth + 1
                    : 1;

                Track(_subagents, id, new SubagentEntry(
                    ReadString(input?["subagent_type"]) ?? "subagent",
                    Redact(ReadString(input?["description"])),
                    depth,
                    sequence));
                break;
        }
    }

    private void ApplyUser(JsonObject evt)
    {
        foreach (JsonObject block in AgentTranscriptFormatter.ContentBlocks(evt))
        {
            if (ReadString(block["type"]) != "tool_result"
                || ReadString(block["tool_use_id"]) is not { } toolUseId
                || !_pendingTools.Remove(toolUseId, out PendingTool? pending))
            {
                continue;
            }

            bool isError = block["is_error"]?.GetValueKind() == JsonValueKind.True;

            if (pending.Name == "TaskCreate" && !isError)
            {
                ApplyTaskCreated(pending, ResolveCreatedTaskId(evt, block));
            }

            // Subagents normally leave on their task notification. One that never produced
            // task events (no task id) leaves when its Agent call returns instead.
            if (AgentTranscriptFormatter.IsAgentTool(pending.Name)
                && !_subagentKeyByTaskId.ContainsValue(toolUseId))
            {
                _subagents.Remove(toolUseId);
            }
        }
    }

    private void ApplySystem(JsonObject evt)
    {
        string? taskId = ReadString(evt["task_id"]);

        switch (ReadString(evt["subtype"]))
        {
            case "task_started" when taskId is not null:
                ApplySubagentStarted(evt, taskId);
                break;

            case "task_progress" when taskId is not null:
                if (FindSubagent(taskId) is { } progressing
                    && ReadString(evt["last_tool_name"]) is { } lastTool)
                {
                    progressing.LastTool = lastTool;
                }

                break;

            case "task_notification" when taskId is not null:
                string status = ReadString(evt["status"]) ?? "completed";

                if (status is not ("running" or "pending")
                    && _subagentKeyByTaskId.Remove(taskId, out string? key))
                {
                    _subagents.Remove(key);
                }

                break;

            // init (also repeated by the claude -c retry), thinking_tokens, hooks and unknown
            // subtypes only advance LastEventAt.
        }
    }

    private void ApplySubagentStarted(JsonObject evt, string taskId)
    {
        string? toolUseId = ReadString(evt["tool_use_id"]);
        string? subagentType = ReadString(evt["subagent_type"]);
        string? description = Redact(ReadString(evt["description"]));
        int? spawnDepth = ReadNumber(evt["spawn_depth"]) is { } depth ? (int)depth : null;

        if (toolUseId is not null && _subagents.TryGetValue(toolUseId, out SubagentEntry? known))
        {
            known.SubagentType = subagentType ?? known.SubagentType;
            known.Description = description ?? known.Description;
            known.SpawnDepth = spawnDepth ?? known.SpawnDepth;
            Track(_subagentKeyByTaskId, taskId, toolUseId);
            return;
        }

        // Tasks also cover background shells; only subagent tasks are tracked here.
        if (subagentType is null)
        {
            return;
        }

        string key = toolUseId ?? $"task:{taskId}";
        Track(_subagents, key, new SubagentEntry(subagentType, description, spawnDepth ?? 1, ++_sequence));
        Track(_subagentKeyByTaskId, taskId, key);
    }

    private SubagentEntry? FindSubagent(string taskId)
    {
        return _subagentKeyByTaskId.TryGetValue(taskId, out string? key)
            && _subagents.TryGetValue(key, out SubagentEntry? subagent)
                ? subagent
                : null;
    }

    private void ApplyTaskCreated(PendingTool pending, string? taskId)
    {
        if (_tasks.Count >= MaxTasks)
        {
            return;
        }

        string id = taskId ?? NextTaskId();
        string subject = Redact(ReadString(pending.Input?["subject"])) ?? $"Task {id}";
        string? activeForm = Redact(ReadString(pending.Input?["activeForm"]));

        _tasks.RemoveAll(t => t.Id == id);
        _tasks.Add(new TaskEntry(id, subject, activeForm, "pending", ++_sequence));
    }

    private void ApplyTaskUpdate(JsonNode? input, long sequence)
    {
        if (ReadId(input?["taskId"]) is not { } id
            || _tasks.Find(t => t.Id == id) is not { } task)
        {
            return;
        }

        string? status = ReadString(input?["status"]);

        if (status == "deleted")
        {
            _tasks.Remove(task);
            return;
        }

        task.Subject = Redact(ReadString(input?["subject"])) ?? task.Subject;
        task.ActiveForm = Redact(ReadString(input?["activeForm"])) ?? task.ActiveForm;

        if (status is not null)
        {
            task.Status = status;
            task.UpdatedSequence = sequence;
        }
    }

    private static string? ResolveCreatedTaskId(JsonObject evt, JsonObject block)
    {
        // Structured result first, then the "Task #N created" text the CLI renders.
        if (ReadId(evt["tool_use_result"]?["task"]?["id"]) is { } structured)
        {
            return structured;
        }

        string? text = AgentTranscriptFormatter.ReadToolResultText(block["content"]);

        if (text is null)
        {
            return null;
        }

        Match match = TaskIdPattern().Match(text);

        return match.Success ? match.Groups["id"].Value : null;
    }

    private string NextTaskId()
    {
        int next = _tasks
            .Select(t => int.TryParse(t.Id, NumberStyles.None, CultureInfo.InvariantCulture, out int n) ? n : 0)
            .DefaultIfEmpty(0)
            .Max() + 1;

        return next.ToString(CultureInfo.InvariantCulture);
    }

    private static string? ReadId(JsonNode? node)
    {
        return ReadString(node)
            ?? (ReadNumber(node) is { } number ? number.ToString(CultureInfo.InvariantCulture) : null);
    }

    private static string? ReadString(JsonNode? node) => AgentTranscriptFormatter.ReadString(node);

    private static decimal? ReadNumber(JsonNode? node) => AgentTranscriptFormatter.ReadNumber(node);

    private static long ReadLong(JsonNode? node)
    {
        return ReadNumber(node) is { } number && number > 0 ? (long)number : 0;
    }

    private static string? Redact(string? text)
    {
        // Source lines are redacted before they arrive; redact again because JSON unescaping
        // can surface a secret shape the escaped form did not match.
        string collapsed = AgentTranscriptFormatter.Collapse(text, MaxSummaryLength);

        return collapsed.Length == 0 ? null : SecretRedactor.Redact(collapsed);
    }

    private static void Track<T>(Dictionary<string, T> map, string key, T value)
    {
        // Bound the correlation state: a run killed mid-tool leaves entries that never resolve.
        if (map.Count >= MaxTrackedEntries && !map.ContainsKey(key))
        {
            map.Clear();
        }

        map[key] = value;
    }

    private sealed record PendingTool(
        string Name,
        string? Summary,
        DateTimeOffset StartedAt,
        long Sequence,
        JsonObject? Input);

    private sealed class SubagentEntry(string subagentType, string? description, int spawnDepth, long sequence)
    {
        public string SubagentType { get; set; } = subagentType;

        public string? Description { get; set; } = description;

        public int SpawnDepth { get; set; } = spawnDepth;

        public string? LastTool { get; set; }

        public long Sequence { get; } = sequence;
    }

    private sealed class TaskEntry(string id, string subject, string? activeForm, string status, long sequence)
    {
        public string Id { get; } = id;

        public string Subject { get; set; } = subject;

        public string? ActiveForm { get; set; } = activeForm;

        public string Status { get; set; } = status;

        public long UpdatedSequence { get; set; } = sequence;

        public AgentTaskItem ToItem() => new(Id, Subject, ActiveForm, Status);
    }

    // Matches the id in the CLI's TaskCreate result text, e.g. "Task #3 created successfully".
    [GeneratedRegex(@"#(?<id>\d+)", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex TaskIdPattern();
}
