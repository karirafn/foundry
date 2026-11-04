namespace Foundry.Modules.Workers.Contracts;

/// <summary>
/// Live, in-memory projection of a running worker's stream-json events, broadcast to the
/// dashboard as the run progresses. Presentation-only: never persisted, rebuilt from the
/// container log after a restart. Text fields are summaries, never tool result bodies.
/// </summary>
public sealed record AgentActivity(
    WorkerRunId WorkerRunId,
    Guid IssueId,
    DateTimeOffset? LastEventAt,
    IReadOnlyList<AgentTaskItem> Tasks,
    AgentTaskItem? CurrentTask,
    int CompletedTaskCount,
    IReadOnlyList<ActiveSubagent> Subagents,
    string? ActiveSkill,
    InFlightTool? InFlightTool,
    AgentTokenTotals Tokens);

/// <summary>An entry in the agent's own task list (<c>TaskCreate</c> / <c>TaskUpdate</c>).</summary>
public sealed record AgentTaskItem(
    string Id,
    string Subject,
    string? ActiveForm,
    string Status);

/// <summary>A subagent that has been spawned and has not yet reported completion.</summary>
public sealed record ActiveSubagent(
    string SubagentType,
    string? Description,
    int SpawnDepth,
    string? LastTool);

/// <summary>The most recent tool call with no matching tool result yet.</summary>
public sealed record InFlightTool(
    string Name,
    string? Summary,
    DateTimeOffset? StartedAt);

/// <summary>Billable token totals for the run so far.</summary>
/// <param name="InputTokens">
/// Billable input tokens (uncached + cache-read + cache-write) summed across streamed assistant
/// messages, de-duplicated by message id.
/// </param>
/// <param name="OutputTokens">
/// Final output tokens from the <c>result</c> event's <c>modelUsage</c>; <see langword="null"/>
/// until the session completes, because streamed messages only carry a start-of-message stub.
/// </param>
public sealed record AgentTokenTotals(
    long InputTokens,
    long? OutputTokens);

/// <summary>Payload broadcast when a run leaves <c>ActiveRun</c> and its projection is disposed.</summary>
public sealed record AgentActivityEnded(
    WorkerRunId WorkerRunId,
    Guid IssueId);
