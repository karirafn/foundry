import {
  AGENT_SILENCE_THRESHOLD_MINUTES,
  AgentActivity,
  InFlightTool,
} from '../../../../core/models/agent-activity.model';
import {
  formatElapsed,
  laneStepText,
  taskOrdinal,
} from '../../../../shared/utils/agent-activity-format';

/** A task-bar segment: a finished task, the one being worked on, or one not started. */
export type TaskSegment = 'done' | 'current' | 'empty';

/** What a worker card's activity panel reads. */
export interface ActivityPanel {
  /** No activity event has arrived yet. */
  readonly starting: boolean;
  readonly taskLine: string;
  /** One per task; empty when the run has no task list. */
  readonly segments: readonly TaskSegment[];
  /** "task 3 of 7"; null when the run has no task list. */
  readonly progress: string | null;
  readonly tool: string;
}

/** The card footer's line on the last event, and whether the run has gone silent. */
export interface LastEventLine {
  readonly text: string;
  readonly silent: boolean;
}

const STARTING_PANEL: ActivityPanel = {
  starting: true,
  taskLine: 'Starting…',
  segments: [],
  progress: null,
  tool: "Waiting for the agent's first event",
};

const SILENCE_THRESHOLD_MS = AGENT_SILENCE_THRESHOLD_MINUTES * 60_000;

/** The panel for a run's live Agent Activity; `null` activity means none has arrived yet. */
export function activityPanel(
  activity: AgentActivity | null,
  latestTool: InFlightTool | null,
): ActivityPanel {
  if (activity === null) {
    return STARTING_PANEL;
  }
  const task = activity.currentTask;
  const total = activity.tasks.length;
  return {
    starting: false,
    // laneStepText appends the position, which the bar shows beside it instead.
    taskLine: task !== null ? (task.activeForm ?? task.subject) : laneStepText(activity),
    segments: activity.tasks.map((t) =>
      t.status === 'completed' ? 'done' : t.id === task?.id ? 'current' : 'empty',
    ),
    progress: total > 0 ? `task ${taskOrdinal(activity)} of ${total}` : null,
    tool: latestTool === null ? 'No tool calls yet' : toolText(latestTool),
  };
}

/** "last event 42s ago", or the silent text at or above the silence threshold; null before any event. */
export function lastEventLine(activity: AgentActivity | null, now: number): LastEventLine | null {
  const since = activity?.lastEventAt ?? null;
  if (activity === null || since === null) {
    return null;
  }
  const elapsedMs = now - Date.parse(since);
  const elapsed = formatElapsed(elapsedMs);
  if (elapsedMs < SILENCE_THRESHOLD_MS) {
    return { text: `last event ${elapsed} ago`, silent: false };
  }
  const tool = activity.inFlightTool?.name ?? null;
  return { text: `silent ${elapsed}${tool !== null ? ` · ${tool} running` : ''}`, silent: true };
}

/**
 * Identifies the run's current silence: the run and the event it has been silent since, so a new
 * event ends the episode. Null while the run is not silent.
 */
export function silenceEpisode(activity: AgentActivity, now: number): string | null {
  const since = activity.lastEventAt;
  if (since === null || now - Date.parse(since) < SILENCE_THRESHOLD_MS) {
    return null;
  }
  return `${activity.workerRunId}@${since}`;
}

function toolText(tool: InFlightTool): string {
  return tool.summary ? `${tool.name} ${tool.summary}` : tool.name;
}
