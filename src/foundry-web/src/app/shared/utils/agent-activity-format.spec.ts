import { AgentActivity } from '../../core/models/agent-activity.model';
import { backgroundTaskCount, backgroundTaskText, formatElapsed, laneStepText } from './agent-activity-format';

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
