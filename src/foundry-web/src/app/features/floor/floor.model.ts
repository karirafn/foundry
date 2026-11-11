import { ActiveSubagent, AgentActivity, InFlightTool } from '../../core/models/agent-activity.model';
import { IssueSummary, LIVE_STATES, PublishPhase } from '../issues/issue.model';

/** One Floor lane: an active run's live Agent Activity with its issue (null until the issue list loads). */
export interface FloorLane {
  readonly issueId: string;
  readonly issue: IssueSummary | null;
  readonly activity: AgentActivity;
  readonly recentTools: readonly InFlightTool[];
}

/** How many queued issues the Floor lists under "Up next". */
export const FLOOR_QUEUE_LIMIT = 5;

/** Subagents shown per group before the rest collapse behind a disclosure. */
export const FLOOR_SUBAGENT_VISIBLE_LIMIT = 3;

/**
 * Builds one lane per active run. Runs come from the live Agent Activity projections; once the issue list
 * has loaded, a run whose issue is no longer live (completed, failed, or not in the active list) is dropped
 * so a missed AgentActivityEnded cannot leave a lane behind.
 */
export function buildFloorLanes(
  activities: readonly AgentActivity[],
  issues: readonly IssueSummary[],
  issuesLoaded: boolean,
  recentToolsFor: (workerRunId: string) => readonly InFlightTool[],
): FloorLane[] {
  const issuesById = new Map(issues.map((issue) => [issue.id, issue]));
  const lanes: FloorLane[] = [];

  for (const activity of activities) {
    const issue = issuesById.get(activity.issueId) ?? null;
    if (issuesLoaded && (issue === null || !LIVE_STATES.has(issue.state))) {
      continue;
    }
    // The agent has exited once the run publishes; the run shows as a publishing slot instead.
    if (issue !== null && issue.publishPhase != null) {
      continue;
    }
    lanes.push({ issueId: activity.issueId, issue, activity, recentTools: recentToolsFor(activity.workerRunId) });
  }

  return lanes;
}

/** A run whose agent has exited and whose commits Foundry is publishing, or retrying to publish. */
export interface PublishingSlot {
  readonly issue: IssueSummary;
  readonly phase: PublishPhase;
}

/** One slot per working issue whose run is publishing; such a run still holds its dispatch slot. */
export function buildPublishingSlots(issues: readonly IssueSummary[]): PublishingSlot[] {
  const slots: PublishingSlot[] = [];
  for (const issue of issues) {
    const phase = issue.publishPhase ?? null;
    if (phase !== null && LIVE_STATES.has(issue.state)) {
      slots.push({ issue, phase });
    }
  }
  return slots;
}

/** Splits active subagents into top-level (parallel) and nested (spawned by another subagent). */
export function splitSubagents(subagents: readonly ActiveSubagent[]): {
  parallel: readonly ActiveSubagent[];
  nested: readonly ActiveSubagent[];
} {
  return {
    parallel: subagents.filter((s) => s.spawnDepth <= 1),
    nested: subagents.filter((s) => s.spawnDepth > 1),
  };
}

/** Compact elapsed time: "12s", "6m", "1h 4m". */
export function formatElapsed(ms: number): string {
  const seconds = Math.max(0, Math.floor(ms / 1000));
  if (seconds < 60) {
    return `${seconds}s`;
  }
  const minutes = Math.floor(seconds / 60);
  if (minutes < 60) {
    return `${minutes}m`;
  }
  const hours = Math.floor(minutes / 60);
  const rest = minutes % 60;
  return rest === 0 ? `${hours}h` : `${hours}h ${rest}m`;
}

/** Compact token count: "950", "12.3k", "1.2M". */
export function formatTokenCount(count: number): string {
  if (count < 1000) {
    return String(count);
  }
  if (count < 1_000_000) {
    return `${trimTrailingZero((count / 1000).toFixed(1))}k`;
  }
  return `${trimTrailingZero((count / 1_000_000).toFixed(1))}M`;
}

function trimTrailingZero(value: string): string {
  return value.endsWith('.0') ? value.slice(0, -2) : value;
}

/**
 * The lane's step for screen readers, e.g. "Running tests, task 3 of 7". It changes only on task or
 * subagent step changes — never on token or tool ticks — so it is safe inside a polite live region.
 */
export function laneStepText(activity: AgentActivity): string {
  const total = activity.tasks.length;
  const task = activity.currentTask;
  if (task !== null) {
    const step = task.activeForm ?? task.subject;
    return total > 0 ? `${step}, task ${taskOrdinal(activity)} of ${total}` : step;
  }
  const subagent = activity.subagents.length > 0 ? activity.subagents[activity.subagents.length - 1] : null;
  if (subagent !== null) {
    return `${subagent.subagentType} subagent working`;
  }
  return total > 0 ? `${activity.completedTaskCount} of ${total} tasks done` : 'Working';
}

/** 1-based position of the current task, counting completed tasks before it. */
export function taskOrdinal(activity: AgentActivity): number {
  return Math.min(activity.completedTaskCount + 1, activity.tasks.length);
}
