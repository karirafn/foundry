/** Minutes without an agent event before a run is styled as silent. */
export const AGENT_SILENCE_THRESHOLD_MINUTES = 5;

/** An entry in the agent's own task list (TaskCreate / TaskUpdate). */
export interface AgentTaskItem {
  readonly id: string;
  readonly subject: string;
  readonly activeForm: string | null;
  readonly status: string;
}

/** A subagent that has been spawned and has not yet reported completion. */
export interface ActiveSubagent {
  readonly subagentType: string;
  readonly description: string | null;
  readonly spawnDepth: number;
  readonly lastTool: string | null;
}

/** The most recent tool call with no matching tool result yet. */
export interface InFlightTool {
  readonly name: string;
  readonly summary: string | null;
  readonly startedAt: string | null;
}

/** Billable token totals; the server computes them, the client never sums token buckets. */
export interface AgentTokenTotals {
  /** Billable input (uncached + cache read + cache write) so far. */
  readonly inputTokens: number;
  /** Final output; null until the run's result event reports it. */
  readonly outputTokens: number | null;
}

/** Live Agent Activity projection for an active run, pushed over the worker hub. */
export interface AgentActivity {
  readonly workerRunId: string;
  readonly issueId: string;
  readonly lastEventAt: string | null;
  readonly tasks: readonly AgentTaskItem[];
  readonly currentTask: AgentTaskItem | null;
  readonly completedTaskCount: number;
  readonly subagents: readonly ActiveSubagent[];
  readonly activeSkill: string | null;
  readonly inFlightTool: InFlightTool | null;
  readonly tokens: AgentTokenTotals;
}

/** Sent when a run leaves ActiveRun and its projection is disposed. */
export interface AgentActivityEnded {
  readonly workerRunId: string;
  readonly issueId: string;
}

/**
 * One-line summary for an issue card, e.g. "developer · Running tests (3/7)":
 * the latest active subagent type (or main agent), the current task and task progress.
 * Without a task list it falls back to the in-flight tool.
 */
export function agentActivitySummary(activity: AgentActivity): string {
  const subagent = activity.subagents.length > 0 ? activity.subagents[activity.subagents.length - 1] : null;
  const agent = subagent?.subagentType ?? 'main agent';
  const total = activity.tasks.length;
  const progress = total > 0 ? `(${activity.completedTaskCount}/${total})` : null;
  const task = activity.currentTask;
  const step = task !== null ? (task.activeForm ?? task.subject) : null;
  const tool = activity.inFlightTool?.name ?? subagent?.lastTool ?? null;

  const detail = step !== null
    ? [step, progress].filter((p) => p !== null).join(' ')
    : [tool, progress].filter((p) => p !== null).join(' ');

  return detail.length > 0 ? `${agent} · ${detail}` : agent;
}
