import { AgentActivity } from '../../core/models/agent-activity.model';
import {
  backgroundTaskCount,
  backgroundTaskText,
  formatElapsed,
  laneStepText,
  taskListItems,
} from './agent-activity-format';

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

describe('formatElapsed', () => {
  it.each([
    [0, '0s'],
    [-5_000, '0s'],
    [42_000, '42s'],
    [6 * 60_000, '6m'],
    [60 * 60_000, '1h'],
    [64 * 60_000, '1h 4m'],
  ])('should format %i ms as %s', (ms, expected) => {
    expect(formatElapsed(ms)).toBe(expected);
  });
});

describe('laneStepText', () => {
  it('should name the current task and its position', () => {
    // Arrange
    const task = { id: '3', subject: 'Run tests', activeForm: 'Running tests', status: 'in_progress' };
    const activity = buildActivity({
      tasks: [
        { id: '1', subject: 'A', activeForm: null, status: 'completed' },
        { id: '2', subject: 'B', activeForm: null, status: 'completed' },
        task,
        { id: '4', subject: 'D', activeForm: null, status: 'pending' },
      ],
      currentTask: task,
      completedTaskCount: 2,
    });

    // Act / Assert
    expect(laneStepText(activity)).toBe('Running tests, task 3 of 4');
  });

  it('should fall back to the latest subagent without a current task', () => {
    // Arrange
    const activity = buildActivity({
      subagents: [{ subagentType: 'developer', description: null, lastTool: 'Bash' }],
    });

    // Act / Assert
    expect(laneStepText(activity)).toBe('developer subagent working');
  });

  it('should not change when only token totals change', () => {
    // Arrange
    const before = buildActivity();
    const after = buildActivity({ tokens: { inputTokens: 5, outputTokens: 9 } });

    // Act / Assert
    expect(laneStepText(after)).toBe(laneStepText(before));
  });
});

describe('backgroundTaskText', () => {
  it('is the task description', () => {
    // Act / Assert
    expect(backgroundTaskText({ taskType: 'local_bash', description: 'dotnet test' })).toBe('dotnet test');
  });

  it.each(['', '   '])('reads "a background task" for the blank description %j', (description) => {
    // Act / Assert
    expect(backgroundTaskText({ taskType: 'local_bash', description })).toBe('a background task');
  });
});

describe('backgroundTaskCount', () => {
  it('is singular for one task', () => {
    // Act / Assert
    expect(backgroundTaskCount(1)).toBe('1 background task');
  });

  it('is plural for several tasks', () => {
    // Act / Assert
    expect(backgroundTaskCount(3)).toBe('3 background tasks');
  });
});

describe('taskListItems', () => {
  const read = { id: '1', subject: 'Read the criteria', activeForm: 'Reading the criteria', status: 'completed' };
  const write = { id: '2', subject: 'Write failing tests', activeForm: 'Writing failing tests', status: 'in_progress' };
  const run = { id: '3', subject: 'Run the suite', activeForm: 'Running the suite', status: 'pending' };

  it('lists every task in order as done, current or upcoming', () => {
    // Arrange
    const activity = buildActivity({ tasks: [read, write, run], currentTask: write, completedTaskCount: 1 });

    // Act
    const items = taskListItems(activity);

    // Assert
    expect(items).toEqual([
      { id: '1', text: 'Read the criteria', state: 'done', subagent: null },
      { id: '2', text: 'Writing failing tests', state: 'current', subagent: null },
      { id: '3', text: 'Run the suite', state: 'upcoming', subagent: null },
    ]);
  });

  it('reads the current task by its subject when it has no active form', () => {
    // Arrange
    const current = { ...write, activeForm: null };
    const activity = buildActivity({ tasks: [current], currentTask: current });

    // Act / Assert
    expect(taskListItems(activity)[0].text).toBe('Write failing tests');
  });

  it('names the latest subagent on the current task only', () => {
    // Arrange
    const activity = buildActivity({
      tasks: [read, write, run],
      currentTask: write,
      subagents: [
        { subagentType: 'Explore', description: null, lastTool: null },
        { subagentType: 'developer', description: null, lastTool: 'Bash' },
      ],
    });

    // Act
    const items = taskListItems(activity);

    // Assert
    expect(items.map((item) => item.subagent)).toEqual([null, 'developer', null]);
  });

  it.each(['', '  '])('names no subagent of the blank type %j', (subagentType) => {
    // Arrange
    const activity = buildActivity({
      tasks: [write],
      currentTask: write,
      subagents: [{ subagentType, description: null, lastTool: null }],
    });

    // Act / Assert
    expect(taskListItems(activity)[0].subagent).toBeNull();
  });

  it('has no current task when the current task is not in the list', () => {
    // Arrange
    const outside = { id: '9', subject: 'Elsewhere', activeForm: null, status: 'in_progress' };
    const activity = buildActivity({ tasks: [read, run], currentTask: outside });

    // Act / Assert
    expect(taskListItems(activity).map((item) => item.state)).toEqual(['done', 'upcoming']);
  });

  it('is empty for a run with no task list', () => {
    // Act / Assert
    expect(taskListItems(buildActivity())).toEqual([]);
  });
});
