import {
  AgentActivity,
  AgentTaskItem,
  BackgroundTask,
} from '../../../../core/models/agent-activity.model';
import {
  TASK_SEGMENT_LIMIT,
  activityPanel,
  lastEventLine,
  silenceEpisode,
} from './worker-activity.model';

function buildActivity(overrides: Partial<AgentActivity> = {}): AgentActivity {
  return {
    workerRunId: 'run-1',
    issueId: 'issue-1',
    role: 'implement',
    lastEventAt: null,
    tasks: [],
    currentTask: null,
    completedTaskCount: 0,
    subagents: [],
    inFlightTool: null,
    tokens: { inputTokens: 0, outputTokens: 0 },
    backgroundTasks: [],
    ...overrides,
  };
}

function task(id: string, status: string, activeForm: string | null = null): AgentTaskItem {
  return { id, subject: `Task ${id}`, activeForm, status };
}

/** `total` tasks with the first `done` completed and, when `working`, the next one in progress. */
function activityWithTasks(total: number, done: number, working: boolean): AgentActivity {
  const tasks = Array.from({ length: total }, (_, i) =>
    task(`${i + 1}`, i < done ? 'completed' : working && i === done ? 'in_progress' : 'pending'),
  );
  return buildActivity({
    tasks,
    currentTask: working ? tasks[done] : null,
    completedTaskCount: done,
  });
}

function bgTask(description: string): BackgroundTask {
  return { taskType: 'local_bash', description };
}

const NOW = Date.parse('2026-01-01T12:00:00Z');

function secondsBefore(seconds: number): string {
  return new Date(NOW - seconds * 1000).toISOString();
}

describe('activityPanel', () => {
  it('shows the starting state before the first activity event', () => {
    // Act
    const panel = activityPanel(null);

    // Assert
    expect(panel).toEqual({
      starting: true,
      taskLine: "Waiting for the agent's first event",
      taskBar: null,
    });
  });

  it("names the current task's active form", () => {
    // Arrange
    const current = task('2', 'in_progress', 'Running tests');
    const activity = buildActivity({
      tasks: [task('1', 'completed'), current, task('3', 'pending')],
      currentTask: current,
      completedTaskCount: 1,
    });

    // Act
    const panel = activityPanel(activity);

    // Assert
    expect(panel.taskLine).toBe('Running tests');
  });

  it("falls back to the current task's subject without an active form", () => {
    // Arrange
    const current = task('1', 'in_progress');
    const activity = buildActivity({ tasks: [current], currentTask: current });

    // Act
    const panel = activityPanel(activity);

    // Assert
    expect(panel.taskLine).toBe('Task 1');
  });

  it('falls back to the working subagent without a current task', () => {
    // Arrange
    const activity = buildActivity({
      subagents: [{ subagentType: 'Explore', description: null, lastTool: null }],
    });

    // Act
    const panel = activityPanel(activity);

    // Assert
    expect(panel.taskLine).toBe('Explore subagent working');
  });

  it('falls back to the done count when the task list has no current task', () => {
    // Arrange
    const activity = buildActivity({
      tasks: [task('1', 'completed'), task('2', 'pending')],
      completedTaskCount: 1,
    });

    // Act
    const panel = activityPanel(activity);

    // Assert
    expect(panel.taskLine).toBe('1 of 2 tasks done');
  });

  it('reads "Working" with no task list and no subagent', () => {
    // Act
    const panel = activityPanel(buildActivity());

    // Assert
    expect(panel.taskLine).toBe('Working');
    expect(panel.starting).toBe(false);
  });

  it('marks one segment per task as done, current or empty, with the position', () => {
    // Arrange
    const current = task('3', 'in_progress', 'Writing docs');
    const activity = buildActivity({
      tasks: [task('1', 'completed'), task('2', 'completed'), current, task('4', 'pending')],
      currentTask: current,
      completedTaskCount: 2,
    });

    // Act
    const panel = activityPanel(activity);

    // Assert
    expect(panel.taskBar).toEqual({
      shape: { kind: 'segments', segments: ['done', 'done', 'current', 'empty'] },
      progress: 'task 3 of 4',
    });
  });

  it('shows a single task as one segment', () => {
    // Arrange
    const activity = activityWithTasks(1, 0, true);

    // Act
    const panel = activityPanel(activity);

    // Assert
    expect(panel.taskBar).toEqual({
      shape: { kind: 'segments', segments: ['current'] },
      progress: 'task 1 of 1',
    });
  });

  it(`keeps segments at the limit of ${TASK_SEGMENT_LIMIT} tasks`, () => {
    // Arrange
    const activity = activityWithTasks(20, 5, true);

    // Act
    const panel = activityPanel(activity);

    // Assert
    expect(panel.taskBar).toEqual({
      shape: {
        kind: 'segments',
        segments: [...Array<string>(5).fill('done'), 'current', ...Array<string>(14).fill('empty')],
      },
      progress: 'task 6 of 20',
    });
  });

  it('turns into a meter at 21 tasks', () => {
    // Arrange
    const activity = activityWithTasks(21, 0, false);

    // Act
    const panel = activityPanel(activity);

    // Assert
    expect(panel.taskBar).toEqual({
      shape: { kind: 'meter', donePercent: 0, currentPercent: 0 },
      progress: 'task 1 of 21',
    });
  });

  it('fills the meter with the completed share and a current slice of one task', () => {
    // Arrange
    const activity = activityWithTasks(25, 5, true);

    // Act
    const panel = activityPanel(activity);

    // Assert
    expect(panel.taskBar).toEqual({
      shape: { kind: 'meter', donePercent: 20, currentPercent: 4 },
      progress: 'task 6 of 25',
    });
  });

  it('has no current slice between tasks', () => {
    // Arrange
    const activity = activityWithTasks(25, 5, false);

    // Act
    const panel = activityPanel(activity);

    // Assert
    expect(panel.taskBar?.shape).toEqual({ kind: 'meter', donePercent: 20, currentPercent: 0 });
  });

  it('fills the meter with no current slice once every task is completed', () => {
    // Arrange
    const activity = activityWithTasks(25, 25, false);

    // Act
    const panel = activityPanel(activity);

    // Assert
    expect(panel.taskBar).toEqual({
      shape: { kind: 'meter', donePercent: 100, currentPercent: 0 },
      progress: 'task 25 of 25',
    });
  });

  it('gives no current slice to a current task that is already completed', () => {
    // Arrange
    const activity = activityWithTasks(25, 25, false);

    // Act
    const panel = activityPanel({ ...activity, currentTask: activity.tasks[24] });

    // Assert
    expect(panel.taskBar?.shape).toEqual({ kind: 'meter', donePercent: 100, currentPercent: 0 });
  });

  it('lights the first segment for a task started before an earlier pending one', () => {
    // Arrange
    const current = task('2', 'in_progress');
    const activity = buildActivity({
      tasks: [task('1', 'pending'), current],
      currentTask: current,
      completedTaskCount: 0,
    });

    // Act
    const panel = activityPanel(activity);

    // Assert
    expect(panel.taskBar).toEqual({
      shape: { kind: 'segments', segments: ['current', 'empty'] },
      progress: 'task 1 of 2',
    });
  });

  it('fills segments from the start for a task completed before an earlier pending one', () => {
    // Arrange
    const activity = buildActivity({
      tasks: [task('1', 'pending'), task('2', 'completed')],
      currentTask: null,
      completedTaskCount: 1,
    });

    // Act
    const panel = activityPanel(activity);

    // Assert
    expect(panel.taskBar).toEqual({
      shape: { kind: 'segments', segments: ['done', 'empty'] },
      progress: 'task 2 of 2',
    });
  });

  it('lights exactly one segment while two tasks are in progress', () => {
    // Arrange
    const current = task('3', 'in_progress');
    const activity = buildActivity({
      tasks: [task('1', 'completed'), task('2', 'in_progress'), current, task('4', 'pending')],
      currentTask: current,
      completedTaskCount: 1,
    });

    // Act
    const panel = activityPanel(activity);

    // Assert
    expect(panel.taskBar).toEqual({
      shape: { kind: 'segments', segments: ['done', 'current', 'empty', 'empty'] },
      progress: 'task 2 of 4',
    });
  });

  it('fills the meter from the completed count rather than recounting task statuses', () => {
    // Arrange
    const activity = activityWithTasks(25, 0, false);

    // Act
    const panel = activityPanel({ ...activity, completedTaskCount: 5 });

    // Assert
    expect(panel.taskBar?.shape).toEqual({ kind: 'meter', donePercent: 20, currentPercent: 0 });
  });

  it('has no task bar with an empty task list', () => {
    // Act
    const panel = activityPanel(buildActivity());

    // Assert
    expect(panel.taskBar).toBeNull();
  });
});

describe('lastEventLine', () => {
  it('is null before the first event', () => {
    // Act
    const line = lastEventLine(buildActivity(), NOW);

    // Assert
    expect(line).toBeNull();
  });

  it('is null without activity', () => {
    // Act
    const line = lastEventLine(null, NOW);

    // Assert
    expect(line).toBeNull();
  });

  it('says how long ago the last event was while under the silence threshold', () => {
    // Act
    const line = lastEventLine(buildActivity({ lastEventAt: secondsBefore(42) }), NOW);

    // Assert
    expect(line).toEqual({ kind: 'recent', text: 'last event 42s ago' });
  });

  it('turns silent at the threshold, naming the running tool', () => {
    // Arrange
    const activity = buildActivity({
      lastEventAt: secondsBefore(5 * 60),
      inFlightTool: { name: 'Bash' },
    });

    // Act
    const line = lastEventLine(activity, NOW);

    // Assert
    expect(line).toEqual({ kind: 'silent', text: 'silent 5m · Bash running' });
  });

  it('turns silent without a running tool', () => {
    // Act
    const line = lastEventLine(buildActivity({ lastEventAt: secondsBefore(7 * 60) }), NOW);

    // Assert
    expect(line).toEqual({ kind: 'silent', text: 'silent 7m' });
  });

  it('names the background task it waits on at the threshold instead of turning silent', () => {
    // Arrange
    const activity = buildActivity({
      lastEventAt: secondsBefore(7 * 60),
      inFlightTool: { name: 'Bash' },
      backgroundTasks: [bgTask('dotnet test')],
    });

    // Act
    const line = lastEventLine(activity, NOW);

    // Assert
    expect(line).toEqual({
      kind: 'waiting',
      command: 'dotnet test',
      more: null,
      elapsed: '7m',
      title: 'dotnet test',
      srText: 'waiting on 1 background task, 7m',
    });
  });

  it('names the first of several background tasks and counts the rest', () => {
    // Arrange
    const activity = buildActivity({
      lastEventAt: secondsBefore(7 * 60),
      backgroundTasks: [bgTask('dotnet test'), bgTask('npm test'), bgTask('')],
    });

    // Act
    const line = lastEventLine(activity, NOW);

    // Assert
    expect(line).toEqual({
      kind: 'waiting',
      command: 'dotnet test',
      more: '+2 more',
      elapsed: '7m',
      title: 'dotnet test\nnpm test\na background task',
      srText: 'waiting on 3 background tasks, 7m',
    });
  });

  it('reads "a background task" when the task has no description', () => {
    // Arrange
    const activity = buildActivity({
      lastEventAt: secondsBefore(7 * 60),
      backgroundTasks: [bgTask('  ')],
    });

    // Act
    const line = lastEventLine(activity, NOW);

    // Assert
    expect(line).toMatchObject({ kind: 'waiting', command: 'a background task' });
  });

  it('says how long ago the last event was below the threshold even with background tasks', () => {
    // Arrange
    const activity = buildActivity({
      lastEventAt: secondsBefore(42),
      backgroundTasks: [bgTask('dotnet test')],
    });

    // Act
    const line = lastEventLine(activity, NOW);

    // Assert
    expect(line).toEqual({ kind: 'recent', text: 'last event 42s ago' });
  });
});

describe('silenceEpisode', () => {
  it('is null while the run waits on background tasks past the threshold', () => {
    // Arrange
    const activity = buildActivity({
      lastEventAt: secondsBefore(7 * 60),
      backgroundTasks: [bgTask('dotnet test')],
    });

    // Act
    const episode = silenceEpisode(activity, NOW);

    // Assert
    expect(episode).toBeNull();
  });

  it('is null while the run is under the threshold', () => {
    // Act
    const episode = silenceEpisode(buildActivity({ lastEventAt: secondsBefore(299) }), NOW);

    // Assert
    expect(episode).toBeNull();
  });

  it('is null before the first event', () => {
    // Act
    const episode = silenceEpisode(buildActivity(), NOW);

    // Assert
    expect(episode).toBeNull();
  });

  it('is keyed by the run and the event it has been silent since', () => {
    // Arrange
    const since = secondsBefore(300);

    // Act
    const episode = silenceEpisode(buildActivity({ lastEventAt: since }), NOW);

    // Assert
    expect(episode).toBe(`run-1@${since}`);
  });
});
