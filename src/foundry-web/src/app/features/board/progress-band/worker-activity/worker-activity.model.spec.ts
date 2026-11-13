import { AgentActivity, AgentTaskItem } from '../../../../core/models/agent-activity.model';
import { activityPanel, lastEventLine, silenceEpisode } from './worker-activity.model';

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
    activeSkill: null,
    inFlightTool: null,
    tokens: { inputTokens: 0, outputTokens: 0 },
    ...overrides,
  };
}

function task(id: string, status: string, activeForm: string | null = null): AgentTaskItem {
  return { id, subject: `Task ${id}`, activeForm, status };
}

const NOW = Date.parse('2026-01-01T12:00:00Z');

function secondsBefore(seconds: number): string {
  return new Date(NOW - seconds * 1000).toISOString();
}

describe('activityPanel', () => {
  it('shows the starting state before the first activity event', () => {
    // Act
    const panel = activityPanel(null, null);

    // Assert
    expect(panel).toEqual({
      starting: true,
      taskLine: 'Starting…',
      segments: [],
      progress: null,
      tool: "Waiting for the agent's first event",
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
    const panel = activityPanel(activity, null);

    // Assert
    expect(panel.taskLine).toBe('Running tests');
  });

  it("falls back to the current task's subject without an active form", () => {
    // Arrange
    const current = task('1', 'in_progress');
    const activity = buildActivity({ tasks: [current], currentTask: current });

    // Act
    const panel = activityPanel(activity, null);

    // Assert
    expect(panel.taskLine).toBe('Task 1');
  });

  it('falls back to the working subagent without a current task', () => {
    // Arrange
    const activity = buildActivity({
      subagents: [{ subagentType: 'Explore', description: null, spawnDepth: 1, lastTool: null }],
    });

    // Act
    const panel = activityPanel(activity, null);

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
    const panel = activityPanel(activity, null);

    // Assert
    expect(panel.taskLine).toBe('1 of 2 tasks done');
  });

  it('reads "Working" with no task list and no subagent', () => {
    // Act
    const panel = activityPanel(buildActivity(), null);

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
    const panel = activityPanel(activity, null);

    // Assert
    expect(panel.segments).toEqual(['done', 'done', 'current', 'empty']);
    expect(panel.progress).toBe('task 3 of 4');
  });

  it('has no bar and no position with an empty task list', () => {
    // Act
    const panel = activityPanel(buildActivity(), null);

    // Assert
    expect(panel.segments).toEqual([]);
    expect(panel.progress).toBeNull();
  });

  it('shows the latest tool with its summary', () => {
    // Act
    const panel = activityPanel(buildActivity(), {
      name: 'Bash',
      summary: 'npm test',
      startedAt: null,
    });

    // Assert
    expect(panel.tool).toBe('Bash npm test');
  });

  it('shows the latest tool alone when it has no summary', () => {
    // Act
    const panel = activityPanel(buildActivity(), { name: 'Read', summary: null, startedAt: null });

    // Assert
    expect(panel.tool).toBe('Read');
  });

  it('says no tool has been called yet', () => {
    // Act
    const panel = activityPanel(buildActivity(), null);

    // Assert
    expect(panel.tool).toBe('No tool calls yet');
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
    expect(line).toEqual({ text: 'last event 42s ago', silent: false });
  });

  it('turns silent at the threshold, naming the running tool', () => {
    // Arrange
    const activity = buildActivity({
      lastEventAt: secondsBefore(5 * 60),
      inFlightTool: { name: 'Bash', summary: 'npm test', startedAt: null },
    });

    // Act
    const line = lastEventLine(activity, NOW);

    // Assert
    expect(line).toEqual({ text: 'silent 5m · Bash running', silent: true });
  });

  it('turns silent without a running tool', () => {
    // Act
    const line = lastEventLine(buildActivity({ lastEventAt: secondsBefore(7 * 60) }), NOW);

    // Assert
    expect(line).toEqual({ text: 'silent 7m', silent: true });
  });
});

describe('silenceEpisode', () => {
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
