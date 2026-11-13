import { AgentActivity } from '../../core/models/agent-activity.model';

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
