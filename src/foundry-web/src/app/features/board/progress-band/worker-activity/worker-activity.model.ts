import {
  AGENT_SILENCE_THRESHOLD_MINUTES,
  AgentActivity,
} from '../../../../core/models/agent-activity.model';
import {
  backgroundTaskCount,
  backgroundTaskText,
  formatElapsed,
  laneStepText,
  taskOrdinal,
} from '../../../../shared/utils/agent-activity-format';

/** A task-bar segment: a finished task, the one being worked on, or one not started. */
export type TaskSegment = 'done' | 'current' | 'empty';

/**
 * Above this many tasks the bar is one continuous meter: at 20 the segments already fill a minimum-width
 * card, so more would push the bar past its label.
 */
export const TASK_SEGMENT_LIMIT = 20;

/** The task bar's shape: one segment per task, or a meter once there are too many tasks for segments. */
export type TaskBar =
  | { readonly kind: 'segments'; readonly segments: readonly TaskSegment[] }
  /** Percentages of the bar's width: the completed tasks, then the one in progress. */
  | { readonly kind: 'meter'; readonly donePercent: number; readonly currentPercent: number };

/** What a worker card's activity panel reads. */
export interface ActivityPanel {
  /** No activity event has arrived yet. */
  readonly starting: boolean;
  readonly taskLine: string;
  /** The bar with its "task 3 of 7" label; null when the run has no task list. */
  readonly taskBar: { readonly shape: TaskBar; readonly progress: string } | null;
}

/** The card footer's line on the last event; `kind` picks its tone. */
export type LastEventLine =
  /** Below the silence threshold: "last event 42s ago". */
  | { readonly kind: 'recent'; readonly text: string }
  /** At or above the threshold with nothing to wait on: a warning. */
  | { readonly kind: 'silent'; readonly text: string }
  /** At or above the threshold while background tasks run: neutral, it is an expected wait. */
  | WaitingLine;

/** "waiting on <command> +2 more · 7m", split so only the command truncates. */
export interface WaitingLine {
  readonly kind: 'waiting';
  /** The first task's description, or "a background task". */
  readonly command: string;
  /** "+2 more"; null with a single task. */
  readonly more: string | null;
  /** Time since the last event, e.g. "7m". */
  readonly elapsed: string;
  /** Every task's text, one per line, for the hover title. */
  readonly title: string;
  /** "waiting on 3 background tasks, 7m": the count, never the raw commands. */
  readonly srText: string;
}

const STARTING_PANEL: ActivityPanel = {
  starting: true,
  taskLine: "Waiting for the agent's first event",
  taskBar: null,
};

const SILENCE_THRESHOLD_MS = AGENT_SILENCE_THRESHOLD_MINUTES * 60_000;

/** The panel for a run's live Agent Activity; `null` activity means none has arrived yet. */
export function activityPanel(activity: AgentActivity | null): ActivityPanel {
  if (activity === null) {
    return STARTING_PANEL;
  }
  const task = activity.currentTask;
  const total = activity.tasks.length;
  return {
    starting: false,
    // laneStepText appends the position, which the bar shows beside it instead.
    taskLine: task !== null ? (task.activeForm ?? task.subject) : laneStepText(activity),
    taskBar:
      total > 0
        ? { shape: taskBar(activity), progress: `task ${taskOrdinal(activity)} of ${total}` }
        : null,
  };
}

function taskBar(activity: AgentActivity): TaskBar {
  const tasks = activity.tasks;
  const current = activity.currentTask;
  if (tasks.length <= TASK_SEGMENT_LIMIT) {
    return {
      kind: 'segments',
      segments: tasks.map((t) =>
        t.status === 'completed' ? 'done' : t.id === current?.id ? 'current' : 'empty',
      ),
    };
  }
  const donePercent = (tasks.filter((t) => t.status === 'completed').length / tasks.length) * 100;
  const working = current !== null && current.status !== 'completed';
  return {
    kind: 'meter',
    donePercent,
    currentPercent: working ? Math.min(100 / tasks.length, 100 - donePercent) : 0,
  };
}

/**
 * "last event 42s ago" below the silence threshold; at or above it, the waiting line while background
 * tasks run, otherwise the silent warning. Null before any event.
 */
export function lastEventLine(activity: AgentActivity | null, now: number): LastEventLine | null {
  const since = activity?.lastEventAt ?? null;
  if (activity === null || since === null) {
    return null;
  }
  const elapsedMs = now - Date.parse(since);
  const elapsed = formatElapsed(elapsedMs);
  if (elapsedMs < SILENCE_THRESHOLD_MS) {
    return { kind: 'recent', text: `last event ${elapsed} ago` };
  }
  const tasks = activity.backgroundTasks;
  if (tasks.length > 0) {
    const texts = tasks.map(backgroundTaskText);
    return {
      kind: 'waiting',
      command: texts[0],
      more: tasks.length > 1 ? `+${tasks.length - 1} more` : null,
      elapsed,
      title: texts.join('\n'),
      srText: `waiting on ${backgroundTaskCount(tasks.length)}, ${elapsed}`,
    };
  }
  const tool = activity.inFlightTool?.name ?? null;
  return { kind: 'silent', text: `silent ${elapsed}${tool !== null ? ` · ${tool} running` : ''}` };
}

/**
 * Identifies the run's current silence: the run and the event it has been silent since, so a new
 * event ends the episode. Null while the run is not silent, including while it waits on background
 * tasks: once they finish and the run stays quiet, the episode starts.
 */
export function silenceEpisode(activity: AgentActivity, now: number): string | null {
  const since = activity.lastEventAt;
  if (since === null || now - Date.parse(since) < SILENCE_THRESHOLD_MS || activity.backgroundTasks.length > 0) {
    return null;
  }
  return `${activity.workerRunId}@${since}`;
}
